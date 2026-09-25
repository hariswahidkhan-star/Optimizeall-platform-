using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.UnitTests.Learning;

public sealed class ExamEngineTests
{
    private static CoursePack Pack => CoursePackLibrary.All.Single(f => f.FileName == "platform-getting-started.json").Pack!;

    [Fact]
    public void Draw_takes_distinct_questions_covers_every_module_and_shuffles_options()
    {
        var pool = Pack.FinalExam!.Pool!;
        for (var seed = 0; seed < 200; seed++)
        {
            var draw = ExamEngine.Draw(pool, 10, new Random(seed));
            Assert.Equal(10, draw.Count);
            Assert.Equal(10, draw.Select(d => d.QuestionId).Distinct().Count());
            var modules = draw.Select(d => pool.Single(q => q.Id == d.QuestionId).Module).Distinct().Count();
            Assert.Equal(2, modules);
            foreach (var d in draw)
            {
                var options = pool.Single(q => q.Id == d.QuestionId).Options!.Count;
                Assert.Equal(Enumerable.Range(0, options), d.OptionOrder.OrderBy(i => i));
            }
        }
    }

    [Fact]
    public void Draws_differ_between_attempts_and_are_reproducible_with_a_seed()
    {
        var pool = Pack.FinalExam!.Pool!;
        var a = ExamEngine.Draw(pool, 10, new Random(1)).Select(d => d.QuestionId + string.Join(",", d.OptionOrder)).ToList();
        var b = ExamEngine.Draw(pool, 10, new Random(1)).Select(d => d.QuestionId + string.Join(",", d.OptionOrder)).ToList();
        Assert.Equal(a, b);
        var distinct = Enumerable.Range(0, 20).Select(s => string.Join("|", ExamEngine.Draw(pool, 10, new Random(s)).Select(d => d.QuestionId))).Distinct().Count();
        Assert.True(distinct > 15);
        // Option orders are shuffled (not always the identity).
        var shuffled = Enumerable.Range(0, 20).SelectMany(s => ExamEngine.Draw(pool, 10, new Random(s))).Count(d => !d.OptionOrder.SequenceEqual(d.OptionOrder.OrderBy(i => i)));
        Assert.True(shuffled > 100);
    }

    [Fact]
    public void Count_is_clamped_to_the_pool()
    {
        var pool = Pack.FinalExam!.Pool!;
        Assert.Equal(pool.Count, ExamEngine.Draw(pool, 500, new Random(3)).Count);
        Assert.Single(ExamEngine.Draw(pool, 0, new Random(3)));
    }

    [Fact]
    public void Shown_positions_map_to_original_indices_and_back()
    {
        var drawn = new DrawnQuestion("q", new[] { 2, 0, 3, 1 });
        Assert.Equal(new[] { 2 }, ExamEngine.ToOriginal(drawn, new[] { 0 }));
        Assert.Equal(new[] { 0, 1 }, ExamEngine.ToOriginal(drawn, new[] { 1, 3 }));
        Assert.Null(ExamEngine.ToOriginal(drawn, new[] { 4 }));
        Assert.Null(ExamEngine.ToOriginal(drawn, new[] { -1 }));
        Assert.Null(ExamEngine.ToOriginal(drawn, new[] { 1, 1 }));
        Assert.Equal(new[] { 1, 3 }, ExamEngine.ToShown(drawn, new[] { 0, 1 }));
    }

    [Fact]
    public void Grading_needs_the_exact_set_and_floors_the_score()
    {
        var pool = new Dictionary<string, PackQuestion>
        {
            ["a"] = new() { Id = "a", Type = "single", Options = new() { "1", "2", "3" }, Correct = new() { 1 } },
            ["b"] = new() { Id = "b", Type = "multiple", Options = new() { "1", "2", "3", "4" }, Correct = new() { 0, 2 } },
            ["c"] = new() { Id = "c", Type = "single", Options = new() { "1", "2", "3" }, Correct = new() { 0 } },
        };
        var draw = pool.Keys.Select(k => new DrawnQuestion(k, new[] { 0, 1, 2 })).ToList();
        var grade = ExamEngine.Grade(draw, new Dictionary<string, int[]> { ["a"] = new[] { 1 }, ["b"] = new[] { 0 } }, pool, 66);
        Assert.Equal(1, grade.CorrectCount); // partial "choose all" is wrong, unanswered is wrong
        Assert.Equal(33, grade.Score); // floor(100/3)
        Assert.False(grade.Passed);
        grade = ExamEngine.Grade(draw, new Dictionary<string, int[]> { ["a"] = new[] { 1 }, ["b"] = new[] { 2, 0 }, ["c"] = new[] { 1 } }, pool, 66);
        Assert.Equal(66, grade.Score);
        Assert.True(grade.Passed);
        Assert.True(grade.Questions.Single(q => q.QuestionId == "b").IsCorrect);
    }

    [Fact]
    public void The_timer_accepts_answers_until_the_deadline_plus_grace()
    {
        var start = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        var deadline = ExamEngine.Deadline(start, 15);
        Assert.Equal(start.AddMinutes(15), deadline);
        Assert.True(ExamEngine.AcceptsAnswers(deadline, deadline));
        Assert.True(ExamEngine.AcceptsAnswers(deadline.AddSeconds(30), deadline));
        Assert.False(ExamEngine.AcceptsAnswers(deadline.AddSeconds(31), deadline));
        Assert.Equal(900, ExamEngine.SecondsRemaining(start, deadline));
        Assert.Equal(0, ExamEngine.SecondsRemaining(deadline.AddMinutes(1), deadline));
    }

    [Fact]
    public void The_attempt_budget_is_a_rolling_24_hours()
    {
        var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal((3, (DateTime?)null), ExamEngine.AttemptBudget(Array.Empty<DateTime>(), now, 3));
        var starts = new[] { now.AddHours(-25), now.AddHours(-20), now.AddHours(-2) };
        Assert.Equal((1, (DateTime?)null), ExamEngine.AttemptBudget(starts, now, 3));
        starts = new[] { now.AddHours(-20), now.AddHours(-10), now.AddHours(-2) };
        var (remaining, next) = ExamEngine.AttemptBudget(starts, now, 3);
        Assert.Equal(0, remaining);
        Assert.Equal(now.AddHours(4), next); // the oldest attempt in the window leaves it 24 h after it started
    }
}
