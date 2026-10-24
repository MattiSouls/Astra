// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.ShaderCompiler.Resources;
using Xunit;
using static SharpEmu.ShaderCompiler.Tests.Resources.ResourceTestProgram;

namespace SharpEmu.ShaderCompiler.Tests.Resources;

// A draw key whose entries are never reused is materialized without the cache for a while; the
// results stay those of a full materialization, and keys whose entries are reused keep hitting.
public sealed class MaterializationBypassTests
{
    // A buffer descriptor straight from user data s[4:7].
    private static ShaderResourcePlan Plan() => Extract(Program(BufferStore(0, 4), EndProgram(8)), userDataCount: 8);

    private static uint[] UserData(uint records) => [0, 0, 0, 0, 0x8000, 0, records, 0x00027FAC];

    private static ResourceSnapshot Run(ResourceMaterializationCache cache, ShaderResourcePlan plan, uint[] userData)
    {
        var snapshot = new ResourceSnapshot();
        var specialization = new ResourceSpecialization();
        Assert.True(cache.Materialize(plan, Inputs(userData), (_, _, _) => false, ref snapshot, ref specialization, out _));
        return snapshot;
    }

    [Fact]
    public void NeverReusedKeysStayCorrectThroughTheBypass()
    {
        var plan = Plan();
        var cache = new ResourceMaterializationCache();
        // Every draw a new record count: no entry is ever reused.
        for (uint draw = 1; draw <= 3000; draw++)
        {
            var snapshot = Run(cache, plan, UserData(draw * 16));
            Assert.Equal(draw * 16, Assert.Single(snapshot.Buffers)[2]);
        }

        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void ReusedKeysKeepHitting()
    {
        var plan = Plan();
        var cache = new ResourceMaterializationCache();
        for (var draw = 0; draw < 3000; draw++)
        {
            var snapshot = Run(cache, plan, UserData(64));
            Assert.Equal(64u, Assert.Single(snapshot.Buffers)[2]);
        }

        Assert.Equal(2999, cache.Hits);
    }
}
