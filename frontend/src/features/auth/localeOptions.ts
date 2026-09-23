import type { SelectOption, SelectOptionGroup } from '@/components/ui/Select';
import { browserTimeZone } from '@/lib/format/dates';
import { browserLocale } from '@/lib/format/locale';

/** ISO 3166-1 alpha-2 codes (officially assigned). Names come from Intl.DisplayNames in the user's language. */
const COUNTRY_CODES =
  'AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW'.split(
    ' ',
  );

/** Shown first for quick selection. */
const COMMON_COUNTRIES = [
  'US',
  'GB',
  'CA',
  'AU',
  'IN',
  'PK',
  'AE',
  'SA',
  'DE',
  'FR',
  'ES',
  'NG',
  'BR',
  'MX',
  'PH',
];

/** Languages the product supports for communication. */
const LANGUAGES = [
  'en',
  'ar',
  'de',
  'es',
  'fr',
  'hi',
  'id',
  'it',
  'ja',
  'ko',
  'nl',
  'pl',
  'pt',
  'ru',
  'tr',
  'ur',
  'zh',
];

function displayNames(type: 'region' | 'language'): Intl.DisplayNames | null {
  try {
    return new Intl.DisplayNames([browserLocale(), 'en'], { type });
  } catch {
    return null;
  }
}

export function countryName(code: string): string {
  return displayNames('region')?.of(code) ?? code;
}

export function countryOptions(): (SelectOption | SelectOptionGroup)[] {
  const names = displayNames('region');
  const option = (code: string): SelectOption => ({ value: code, label: names?.of(code) ?? code });
  const all = COUNTRY_CODES.map(option).sort((a, b) => a.label.localeCompare(b.label));
  return [
    { label: 'Common', options: COMMON_COUNTRIES.map(option) },
    { label: 'All countries', options: all },
  ];
}

/** Region from the browser locale (e.g. en-GB → GB), when it's a known country. */
export function defaultCountry(): string {
  try {
    const locale = new Intl.Locale(browserLocale()).maximize();
    const region = locale.region?.toUpperCase();
    return region && COUNTRY_CODES.includes(region) ? region : '';
  } catch {
    return '';
  }
}

export function languageOptions(): SelectOption[] {
  const names = displayNames('language');
  return LANGUAGES.map((code) => ({ value: code, label: names?.of(code) ?? code })).sort((a, b) =>
    a.label.localeCompare(b.label),
  );
}

export function defaultLanguage(): string {
  const base = browserLocale().split('-')[0]?.toLowerCase() ?? 'en';
  return LANGUAGES.includes(base) ? base : 'en';
}

export function timeZoneOptions(current: string): SelectOption[] {
  let zones: string[] = [];
  try {
    zones = Intl.supportedValuesOf('timeZone');
  } catch {
    zones = [];
  }
  const set = new Set(['UTC', ...zones]);
  if (current) set.add(current);
  return [...set].sort().map((zone) => ({ value: zone, label: zone.replace(/_/g, ' ') }));
}

export function defaultTimeZone(): string {
  return browserTimeZone();
}
