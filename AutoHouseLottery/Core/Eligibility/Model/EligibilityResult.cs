namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct EligibilityResult
{
    public EligibilityResult(EligibilityReason blockers, EligibilityReason unresolved, DateTime? evidenceUtc)
        : this(true, blockers, unresolved, evidenceUtc)
    {
    }

    private EligibilityResult(bool evaluated, EligibilityReason blockers, EligibilityReason unresolved, DateTime? evidenceUtc)
    {
        Evaluated = evaluated;
        Blockers = blockers;
        Unresolved = unresolved;
        EvidenceUtc = evidenceUtc;
    }

    public bool Evaluated { get; }

    public EligibilityReason Blockers { get; }

    public EligibilityReason Unresolved { get; }

    public DateTime? EvidenceUtc { get; }

    public EligibilityVerdict Verdict
    {
        get
        {
            if (!Evaluated)
            {
                return EligibilityVerdict.Unknown;
            }

            if (Blockers != EligibilityReason.None)
            {
                return EligibilityVerdict.Ineligible;
            }

            return Unresolved == EligibilityReason.None ? EligibilityVerdict.Eligible : EligibilityVerdict.Unknown;
        }
    }

    public EligibilityResult Combine(in EligibilityResult other)
    {
        var evidence = EvidenceUtc is { } own && (other.EvidenceUtc is not { } theirs || own < theirs) ? EvidenceUtc : other.EvidenceUtc;
        return new EligibilityResult(Evaluated && other.Evaluated, Blockers | other.Blockers, Unresolved | other.Unresolved, evidence);
    }
}
