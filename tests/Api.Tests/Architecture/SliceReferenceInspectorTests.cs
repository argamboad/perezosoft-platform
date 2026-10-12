namespace Perezosoft.Api.Tests.Architecture;

/// <summary>
/// Proves the slice-isolation scan (<see cref="SliceReferenceInspector"/>, used by
/// <c>FeatureFolders_DoNotReferenceEachOthersNamespaces</c>) matches a slice's namespace on a boundary. v4 audit
/// finding ADV-P4-12: a plain substring match read <c>Features.Reports2</c> as a reference to <c>Reports</c>, so
/// two slices whose names share a prefix failed the gate with no violation between them.
/// </summary>
public class SliceReferenceInspectorTests
{
    [Fact]
    public void ASliceWhoseNameExtendsAnother_IsNotAReferenceToIt()
    {
        const string reports2 = """
            namespace Perezosoft.Api.Features.Reports2;
            using Perezosoft.Api.Features.Reports2.Dto;
            """;

        Assert.Empty(SliceReferenceInspector.ReferencedSlices(reports2, ["Reports"]));
    }

    [Theory]
    [InlineData("using Perezosoft.Api.Features.Reports;")]
    [InlineData("using Perezosoft.Api.Features.Reports.Dto;")]
    [InlineData("var x = new Perezosoft.Api.Features.Reports.ReportRow();")]
    [InlineData("using static Perezosoft.Api.Features.Reports.ReportEndpoints;")]
    public void ARealReference_IsStillCaught(string line)
    {
        var source = "namespace Perezosoft.Api.Features.Reports2;\n" + line;

        Assert.Equal(["Reports"], SliceReferenceInspector.ReferencedSlices(source, ["Reports"]));
    }

    [Fact]
    public void AShorterSlice_ReferencingTheLongerOne_IsCaught()
    {
        const string reports = """
            namespace Perezosoft.Api.Features.Reports;
            using Perezosoft.Api.Features.Reports2;
            """;

        Assert.Equal(["Reports2"], SliceReferenceInspector.ReferencedSlices(reports, ["Reports2"]));
    }
}
