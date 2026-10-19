using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public class LimitCheckTests
{
    [Fact]
    public void Within_limit_does_not_throw()
    {
        LimitCheck.Check("MAX_MAP_TEST", 10, 10);
        LimitCheck.CheckRenderLimits(shaders: 2, surfaces: 3, verts: 100, indexes: 300, leafSurfaces: 3);
    }

    [Fact]
    public void Exceeded_limit_names_lump_and_values()
    {
        var ex = Assert.Throws<BspLimitException>(() => LimitCheck.Check("MAX_MAP_TEST", 11, 10));
        Assert.Contains("MAX_MAP_TEST", ex.Message);
        Assert.Contains("10", ex.Message);
        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void Render_limits_name_the_offending_lump()
    {
        var ex = Assert.Throws<BspLimitException>(() =>
            LimitCheck.CheckRenderLimits(1, 1, 10, Limits.MaxDrawIndexes + 1, 1));
        Assert.Contains("MAX_MAP_DRAW_INDEXES", ex.Message);
    }

    [Fact]
    public void Swgl_profile_does_not_enforce_the_q3map_draw_vert_and_index_limits_but_vanilla_does()
    {
        // 2 952 061 sommets et ~8,8 M d index : refuses a tort par l ancien controle (constantes de compilateur, pas de limite du moteur).
        LimitCheck.CheckRenderLimits(1, 1, 2_952_061, 8_856_183, 1, Limits.Swgl.MaxLeafSurfaces, Limits.Swgl.MaxDrawVertices, Limits.Swgl.MaxDrawIndices);
        var ex = Assert.Throws<BspLimitException>(() =>
            LimitCheck.CheckRenderLimits(1, 1, 2_952_061, 30, 1, Limits.Vanilla.MaxLeafSurfaces, Limits.Vanilla.MaxDrawVertices, Limits.Vanilla.MaxDrawIndices));
        Assert.Contains("MAX_MAP_DRAW_VERTS", ex.Message);
        // Le lump reste borne : un int32 d octets (80 octets par sommet).
        Assert.True((long)Limits.Swgl.MaxDrawVertices * 80 < int.MaxValue);
        Assert.True((long)Limits.Swgl.MaxDrawIndices * 4 < int.MaxValue);
    }

    [Fact]
    public void Swgl_leaf_surfaces_cover_the_endar_spire_assembly_and_name_the_lump_beyond()
    {
        // end_m01aa + end_m01ab : 1 242 104 leaf faces (au-dela de 0x100000).
        LimitCheck.CheckRenderLimits(1, 1, 10, 30, 1_242_104, Limits.Swgl.MaxLeafSurfaces);
        var ex = Assert.Throws<BspLimitException>(() =>
            LimitCheck.CheckRenderLimits(1, 1, 10, 30, Limits.Swgl.MaxLeafSurfaces + 1, Limits.Swgl.MaxLeafSurfaces));
        Assert.Contains("MAX_MAP_LEAFFACES", ex.Message);
        Assert.Throws<BspLimitException>(() =>
            LimitCheck.CheckRenderLimits(1, 1, 10, 30, 1_242_104, Limits.Vanilla.MaxLeafSurfaces));
    }

    [Fact]
    public void Rtx_primitive_estimate_is_checked_against_the_profile_limit()
    {
        // war_planworking avant correction : 747 millions de primitives (145 Gio), plantage du jeu au chargement.
        LimitCheck.CheckRtxEstimate(Limits.RtxPrimitiveWarnThreshold, Limits.Swgl.MaxRtxPrimitives);
        LimitCheck.CheckRtxEstimate(460_805_025, Limits.Swgl.MaxRtxPrimitives); // korr_m35aa (KOTOR) charge
        var ex = Assert.Throws<BspLimitException>(() => LimitCheck.CheckRtxEstimate(747_013_167, Limits.Swgl.MaxRtxPrimitives));
        Assert.Contains("RTX_PRIMITIVE_ESTIMATE", ex.Message);
        LimitCheck.CheckRtxEstimate(747_013_167, Limits.Vanilla.MaxRtxPrimitives); // pas de lancer de rayons en JA vanilla
    }
}
