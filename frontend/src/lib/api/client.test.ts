import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem, session } from '@/test/fetchMock';
import { api, filenameFromContentDisposition, onSessionEvent, tokenStore } from './client';
import { ApiError } from './errors';

describe('api client', () => {
  it('sends JSON with the bearer token, credentials and X-Requested-With', async () => {
    tokenStore.set('tok-1');
    const { fn, calls } = mockFetch({ 'POST /things': () => json(200, { ok: true }) });

    const result = await api.post<{ ok: boolean }>(
      '/things',
      { name: 'x' },
      { query: { page: 2, search: '' } },
    );

    expect(result).toEqual({ ok: true });
    const [url, init] = fn.mock.calls[0]!;
    expect(url).toBe('/api/v1/things?page=2');
    expect(init?.credentials).toBe('include');
    expect(calls[0]!.headers).toMatchObject({
      Authorization: 'Bearer tok-1',
      'X-Requested-With': 'fetch',
      'Content-Type': 'application/json',
    });
    expect(calls[0]!.body).toEqual({ name: 'x' });
  });

  it('returns undefined for 204 responses', async () => {
    mockFetch({ 'DELETE /things/1': () => new Response(null, { status: 204 }) });
    await expect(api.delete('/things/1')).resolves.toBeUndefined();
  });

  it('refreshes once for concurrent 401s and retries each request once', async () => {
    tokenStore.set('expired');
    let refreshCount = 0;
    const { calls } = mockFetch({
      'POST /auth/refresh': async () => {
        refreshCount += 1;
        await new Promise((r) => setTimeout(r, 10));
        return json(200, session(undefined, 'fresh'));
      },
      'GET /a': (req) =>
        req.headers.Authorization === 'Bearer fresh'
          ? json(200, 'a')
          : problem(401, 'auth.unauthorized', 'No'),
      'GET /b': (req) =>
        req.headers.Authorization === 'Bearer fresh'
          ? json(200, 'b')
          : problem(401, 'auth.unauthorized', 'No'),
      'GET /c': (req) =>
        req.headers.Authorization === 'Bearer fresh'
          ? json(200, 'c')
          : problem(401, 'auth.unauthorized', 'No'),
    });
    const refreshed = vi.fn();
    const off = onSessionEvent(refreshed);

    const results = await Promise.all([api.get('/a'), api.get('/b'), api.get('/c')]);

    off();
    expect(results).toEqual(['a', 'b', 'c']);
    expect(refreshCount).toBe(1);
    expect(tokenStore.get()).toBe('fresh');
    expect(refreshed).toHaveBeenCalledWith(expect.objectContaining({ type: 'refreshed' }));
    // 3 original + 1 refresh + 3 retries
    expect(calls).toHaveLength(7);
    expect(calls.find((c) => c.path === '/auth/refresh')!.headers['X-Requested-With']).toBe('fetch');
  });

  it('retries only once: a second 401 after refresh is surfaced', async () => {
    tokenStore.set('expired');
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(undefined, 'fresh')),
      'GET /forbidden': () => problem(401, 'auth.unauthorized', 'Still no'),
    });
    await expect(api.get('/forbidden')).rejects.toMatchObject({ status: 401 });
    expect(calls.filter((c) => c.path === '/forbidden')).toHaveLength(2);
    expect(calls.filter((c) => c.path === '/auth/refresh')).toHaveLength(1);
  });

  it('clears the session and emits session-expired when refresh fails', async () => {
    tokenStore.set('expired');
    mockFetch({
      'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Your session has expired.'),
      'GET /me/home': () => problem(401, 'auth.unauthorized', 'No'),
    });
    const listener = vi.fn();
    const off = onSessionEvent(listener);

    await expect(api.get('/me/home')).rejects.toMatchObject({ status: 401, code: 'auth.session_expired' });

    off();
    expect(listener).toHaveBeenCalledWith({ type: 'session-expired' });
    expect(tokenStore.get()).toBeNull();
  });

  it('retries the refresh once when it lost a rotation race, and keeps the session', async () => {
    tokenStore.set('expired');
    let refreshes = 0;
    mockFetch({
      'POST /auth/refresh': () => {
        refreshes += 1;
        return refreshes === 1
          ? problem(401, 'auth.refresh_race', 'Your session was refreshed in another tab. Retry the request.')
          : json(200, session(undefined, 'fresh'));
      },
      'GET /me/home': (req) =>
        req.headers.Authorization === 'Bearer fresh'
          ? json(200, 'home')
          : problem(401, 'auth.unauthorized', 'No'),
    });
    const listener = vi.fn();
    const off = onSessionEvent(listener);

    await expect(api.get('/me/home')).resolves.toBe('home');

    off();
    expect(refreshes).toBe(2);
    expect(listener).not.toHaveBeenCalledWith({ type: 'session-expired' });
    expect(tokenStore.get()).toBe('fresh');
  });

  it('gives up after one retry when the refresh race repeats', async () => {
    tokenStore.set('expired');
    let refreshes = 0;
    mockFetch({
      'POST /auth/refresh': () => {
        refreshes += 1;
        return problem(401, 'auth.refresh_race', 'Retry the request.');
      },
      'GET /me/home': () => problem(401, 'auth.unauthorized', 'No'),
    });
    await expect(api.get('/me/home')).rejects.toMatchObject({ status: 401, code: 'auth.session_expired' });
    expect(refreshes).toBe(2);
  });

  it('never refreshes for auth endpoints such as login', async () => {
    const { calls } = mockFetch({
      'POST /auth/login': () =>
        problem(401, 'auth.invalid_credentials', 'The email or password is incorrect.'),
    });
    await expect(api.post('/auth/login', { email: 'a@b.co', password: 'x' })).rejects.toMatchObject({
      code: 'auth.invalid_credentials',
    });
    expect(calls).toHaveLength(1);
  });

  it('parses RFC 7807 problems into ApiError with normalized field errors and trace id', async () => {
    mockFetch({
      'POST /auth/register': () =>
        json(400, {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { Email: ['The Email field is not a valid e-mail address.'], '$.countryCode': ['Bad'] },
          traceId: 'abc',
        }),
    });
    const error = await api.post('/auth/register', {}).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.code).toBe('validation_failed');
    expect(apiError.traceId).toBe('abc');
    expect(apiError.errors).toEqual({
      email: ['The Email field is not a valid e-mail address.'],
      countryCode: ['Bad'],
    });
    expect(apiError.fieldError('email')).toMatch(/not a valid/);
  });

  it('keeps domain codes and titles', async () => {
    mockFetch({
      'POST /auth/register': () =>
        problem(400, 'auth.weak_password', 'Choose a stronger password.', {
          errors: { password: ['This password is too common.'] },
        }),
    });
    await expect(api.post('/auth/register', {})).rejects.toMatchObject({
      code: 'auth.weak_password',
      title: 'Choose a stronger password.',
      errors: { password: ['This password is too common.'] },
      traceId: 'trace-123',
    });
  });

  it('maps network failures to a status-0 network_error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    await expect(api.get('/anything')).rejects.toMatchObject({ status: 0, code: 'network_error' });
  });

  it('uploads FormData without forcing a JSON content type', async () => {
    const { calls } = mockFetch({ 'POST /files': () => json(201, { id: 'f1' }) });
    const form = new FormData();
    form.append('file', new Blob(['x'], { type: 'image/png' }), 'shot.png');
    await api.upload('/files', form);
    expect(calls[0]!.headers['Content-Type']).toBeUndefined();
    expect(calls[0]!.body).toBeInstanceOf(FormData);
  });

  it('downloads a blob using the Content-Disposition filename', async () => {
    mockFetch({
      'GET /export': () =>
        new Response('a,b', {
          status: 200,
          headers: {
            'Content-Type': 'text/csv',
            'Content-Disposition': "attachment; filename*=UTF-8''ledger%202026.csv",
          },
        }),
    });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    let downloadName = '';
    click.mockImplementation(function (this: HTMLAnchorElement) {
      downloadName = this.download;
    });
    await api.download('/export', 'fallback.csv');
    expect(downloadName).toBe('ledger 2026.csv');
  });

  it('parses Content-Disposition variants', () => {
    expect(filenameFromContentDisposition('attachment; filename="report.csv"')).toBe('report.csv');
    expect(filenameFromContentDisposition('attachment; filename=plain.csv')).toBe('plain.csv');
    expect(filenameFromContentDisposition(null)).toBeNull();
  });
});
