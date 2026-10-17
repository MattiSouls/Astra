// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Reflection;
using System.Runtime.CompilerServices;
using SharpEmu.Libs.Gpu.GpuCommands.Registers;
using SharpEmu.Libs.Gpu.Rendering;
using Xunit;

namespace SharpEmu.Libs.Tests.Gpu.Rendering;

public sealed class ParallelProgramPrefetchOwnershipTests
{
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    public void ConsumedOrRejectedJobsReleaseProofsAfterValidation(int state, bool current, bool expected)
    {
        var host = new ProofHost { Current = current };
        var prefetch = (ParallelProgramPrefetch)RuntimeHelpers.GetUninitializedObject(typeof(ParallelProgramPrefetch));
        typeof(ParallelProgramPrefetch).GetField("_concurrent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(prefetch, host);
        var banks = new RegisterBanks(static message => new InvalidOperationException(message));
        var inputs = GraphicsProgramInputs.Create();
        var job = new ParallelProgramPrefetch.Job(banks, 0)
        {
            State = state, Inputs = inputs, Assumptions = host.Proofs,
            Programs = (GraphicsPrograms)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsPrograms)),
        };
        prefetch.Current = job;

        Assert.Equal(expected, prefetch.TryTakeGraphics(banks, in inputs, out _));
        Assert.Equal(state == 1, host.Checked);
        Assert.Equal(1, host.Releases);
        Assert.Null(job.Assumptions);
        Assert.False(prefetch.TryTakeGraphics(banks, in inputs, out _));
        Assert.Equal(1, host.Releases);
    }

    private sealed class ProofHost : IConcurrentResolveHost
    {
        public readonly object Proofs = new();
        public bool Current;
        public bool Checked;
        public int Releases;
        public void BeginConcurrentResolve() { }
        public object EndConcurrentResolve() => Proofs;
        public bool IsStillCurrent(object assumptions)
        {
            Assert.Same(Proofs, assumptions);
            Assert.Equal(0, Releases);
            Checked = true;
            return Current;
        }
        public void ReleaseAssumptions(object assumptions)
        {
            Assert.Same(Proofs, assumptions);
            Releases++;
        }
    }
}
