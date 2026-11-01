using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Shouldly;

namespace EventTests.Projections;

/// <summary>
/// [NaturalKeySource] methods that discovery used to skip without a word now fail at configuration time.
/// </summary>
public class NaturalKeySourceSignatureChecksTests
{
    [Fact]
    public void a_non_public_source_fails()
    {
        var ex = Should.Throw<InvalidProjectionException>(
            () => new NksPrivateSourceProjection().AssembleAndAssertValidity());

        ex.Message.ShouldContain("KeyFor()");
        ex.Message.ShouldContain("it is not public");
    }

    [Fact]
    public void two_sources_for_one_event_fail()
    {
        var ex = Should.Throw<InvalidProjectionException>(
            () => new NksTwoSourcesProjection().AssembleAndAssertValidity());

        ex.Message.ShouldContain("also derives the natural key for this event");
    }

    [Fact]
    public void a_source_on_the_aggregate_and_one_on_the_projection_for_one_event_fail()
    {
        var ex = Should.Throw<InvalidProjectionException>(
            () => new NksSplitSourcesProjection().AssembleAndAssertValidity());

        ex.Message.ShouldContain("also derives the natural key for this event");
    }

    [Fact]
    public void two_sources_that_read_the_same_event_property_are_allowed()
    {
        // The Create-then-Apply pairing: both read the key straight off NksCodeAssigned.Code
        var projection = new NksCreateAndApplyProjection();

        Should.NotThrow(() => projection.AssembleAndAssertValidity());
        var mapping = projection.NaturalKeyDefinition!.EventMappings.ShouldHaveSingleItem();
        mapping.Extractor(new Event<NksCodeAssigned>(new NksCodeAssigned(new NksCode("C-1"))))
            .ShouldBe(new NksCode("C-1"));
    }

    [Fact]
    public void a_source_that_computes_the_key_conflicts_with_one_that_reads_it()
    {
        var ex = Should.Throw<InvalidProjectionException>(
            () => new NksReadAndComputeProjection().AssembleAndAssertValidity());

        ex.Message.ShouldContain("also derives the natural key for this event");
    }

    [Fact]
    public void an_explicit_registration_clears_both()
    {
        var privateSource = new NksPrivateSourceProjection();
        privateSource.NaturalKeyFor(x => x.SetBy<NksCodeAssigned>(e => e.Code));
        Should.NotThrow(() => privateSource.AssembleAndAssertValidity());

        var twoSources = new NksTwoSourcesProjection();
        twoSources.NaturalKeyFor(x => x.SetBy<NksCodeAssigned>(e => e.Code));
        Should.NotThrow(() => twoSources.AssembleAndAssertValidity());
        twoSources.NaturalKeyDefinition!.EventMappings.ShouldHaveSingleItem();
    }

    [Fact]
    public void sources_on_an_aggregate_with_only_mapped_keys_fail()
    {
        var ex = Should.Throw<InvalidProjectionException>(
            () => new NksMappedOnlyWithSourceProjection().AssembleAndAssertValidity());

        ex.Message.ShouldContain("only has mapped natural keys");
        ex.Message.ShouldContain("NksMappedOnlyWithSource.KeyFor");
    }

    [Fact]
    public void an_aggregate_with_only_mapped_keys_and_no_sources_is_fine()
    {
        Should.NotThrow(() => new MnkOrderSummaryProjection().AssembleAndAssertValidity());
    }
}

// ───────────────────────── fixtures ─────────────────────────

public record NksCode(string Value);

public record NksCodeAssigned(NksCode Code);

public sealed record NksPrivateSource
{
    public Guid Id { get; set; }

    [NaturalKey]
    public NksCode Code { get; set; } = default!;

    [NaturalKeySource]
    private static NksCode KeyFor(NksCodeAssigned e) => e.Code;
}

public class NksPrivateSourceProjection : SingleStreamProjection<NksPrivateSource, Guid>
{
    public override NksPrivateSource? Evolve(NksPrivateSource? snapshot, Guid id, IEvent e) => snapshot;
}

public sealed record NksTwoSources
{
    public Guid Id { get; set; }

    [NaturalKey]
    public NksCode Code { get; set; } = default!;

    [NaturalKeySource]
    public static NksCode KeyFor(NksCodeAssigned e) => e.Code;

    [NaturalKeySource]
    public static NksCode OtherKeyFor(IEvent<NksCodeAssigned> e) => new(e.Data.Code.Value.ToUpperInvariant());
}

public class NksTwoSourcesProjection : SingleStreamProjection<NksTwoSources, Guid>
{
    public override NksTwoSources? Evolve(NksTwoSources? snapshot, Guid id, IEvent e) => snapshot;
}

public sealed record NksSplitSources
{
    public Guid Id { get; set; }

    [NaturalKey]
    public NksCode Code { get; set; } = default!;

    [NaturalKeySource]
    public static NksCode KeyFor(NksCodeAssigned e) => e.Code;
}

public class NksSplitSourcesProjection : SingleStreamProjection<NksSplitSources, Guid>
{
    [NaturalKeySource]
    public static NksCode KeyFor(NksCodeAssigned e) => e.Code;

    public override NksSplitSources? Evolve(NksSplitSources? snapshot, Guid id, IEvent e) => snapshot;
}

public sealed class NksCreateAndApply
{
    public Guid Id { get; set; }

    [NaturalKey]
    public NksCode Code { get; set; } = default!;

    public string Label { get; set; } = string.Empty;

    [NaturalKeySource]
    public static NksCreateAndApply Create(NksCodeAssigned e) => new() { Code = e.Code, Label = "created" };

    [NaturalKeySource]
    public void Apply(NksCodeAssigned e)
    {
        Code = e.Code;
        Label = "applied";
    }
}

// Aggregates through the conventional Create/Apply methods on NksCreateAndApply
public class NksCreateAndApplyProjection : SingleStreamProjection<NksCreateAndApply, Guid>;

public sealed class NksReadAndCompute
{
    public Guid Id { get; set; }

    [NaturalKey]
    public NksCode Code { get; set; } = default!;

    // Reads the key off the event body
    [NaturalKeySource]
    public void Apply(NksCodeAssigned e) => Code = e.Code;

    // Computes a different key from the same event
    [NaturalKeySource]
    public static NksCode KeyFor(IEvent<NksCodeAssigned> e) => new(e.Data.Code.Value.ToUpperInvariant());
}

public class NksReadAndComputeProjection : SingleStreamProjection<NksReadAndCompute, Guid>
{
    public override NksReadAndCompute? Evolve(NksReadAndCompute? snapshot, Guid id, IEvent e) => snapshot;
}

public sealed record NksMappedOnlyWithSource
{
    public Guid Id { get; set; }

    [MappedNaturalKey<MnkOrder>]
    public MnkOrderNumber Number { get; set; } = default!;

    [NaturalKeySource]
    public static MnkOrderNumber KeyFor(MnkOrderPlaced e) => e.Number;
}

public class NksMappedOnlyWithSourceProjection : SingleStreamProjection<NksMappedOnlyWithSource, Guid>
{
    public override NksMappedOnlyWithSource? Evolve(NksMappedOnlyWithSource? snapshot, Guid id, IEvent e) => snapshot;
}
