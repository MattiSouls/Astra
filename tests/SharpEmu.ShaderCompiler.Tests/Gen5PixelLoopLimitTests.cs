// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Resources;
using SharpEmu.ShaderCompiler.Tests.Resources;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.ShaderCompiler.Tests;

// A pixel shader's structured loop is bounded by the pixel iteration limit, not the
// dispatcher's much larger one.
public sealed class Gen5PixelLoopLimitTests
{
    private const uint OpConstant = 43;

    // s0 += 1 until it carries: one structured loop.
    private static Gen5ShaderProgram Loop() => new(0x1000,
    [
        ResourceTestProgram.Sop2(0, "SAddU32", 0, Gen5Operand.Scalar(0), ResourceTestProgram.Operand(1)),
        new(4, Gen5ShaderEncoding.Sopp, "SCbranchScc0", [0xFFFEu], [], [], null),
        new(8, Gen5ShaderEncoding.Sopp, "SEndpgm", [0u], [], [], null),
    ]);

    private static HashSet<uint> Constants(Gen5ShaderProgram program)
    {
        var (plan, resources, layout) = ResourceTestProgram.Prepare(program, ShaderStage.Pixel, userDataCount: 0);
        var request = new ShaderCompileRequest(plan, resources, layout)
        {
            PixelOutputs = [new Gen5PixelOutputBinding(0, 0, Gen5PixelOutputKind.Float)],
        };
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var words = new uint[shader.Spirv.Length / 4];
        Buffer.BlockCopy(shader.Spirv, 0, words, 0, shader.Spirv.Length);
        var constants = new HashSet<uint>();
        for (var index = 5; index < words.Length; index += (int)(words[index] >> 16))
        {
            if ((words[index] & 0xFFFF) == OpConstant && words[index] >> 16 == 4)
                constants.Add(words[index + 3]);
        }

        return constants;
    }

    [Fact]
    public void PixelLoopEndsAtThePixelIterationLimit()
    {
        var constants = Constants(Loop());
        Assert.Contains(8192u, constants);
        Assert.DoesNotContain(100_000u, constants);
    }
}
