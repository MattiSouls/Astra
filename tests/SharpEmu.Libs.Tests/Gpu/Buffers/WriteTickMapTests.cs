// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu.Buffers;
using Xunit;

namespace SharpEmu.Libs.Tests.Gpu.Buffers;

public sealed class WriteTickMapTests
{
    [Fact]
    public void ARangeReportsOnlyTheWritesThatReachIt()
    {
        var map = new WriteTickMap();
        map.Assign(0x000, 0x100, 5);
        map.Assign(0x800, 0x100, 9);

        Assert.Equal(5UL, map.MaxTick(0x000, 0x100));
        Assert.Equal(9UL, map.MaxTick(0x880, 0x10));
        Assert.Equal(0UL, map.MaxTick(0x200, 0x400));
        Assert.Equal(9UL, map.MaxTick(0x000, 0x1000));
    }

    [Fact]
    public void ANewerWriteSplitsTheOlderRange()
    {
        var map = new WriteTickMap();
        map.Assign(0x000, 0x300, 3);
        map.Assign(0x100, 0x100, 7);

        Assert.Equal(3, map.Count);
        Assert.Equal(3UL, map.MaxTick(0x000, 0x100));
        Assert.Equal(7UL, map.MaxTick(0x180, 0x8));
        Assert.Equal(3UL, map.MaxTick(0x200, 0x100));
    }

    [Fact]
    public void AdjacentWritesOfOneTickMerge()
    {
        var map = new WriteTickMap();
        map.Assign(0x000, 0x100, 4);
        map.Assign(0x200, 0x100, 4);
        map.Assign(0x100, 0x100, 4);

        Assert.Equal(1, map.Count);
        Assert.Equal(4UL, map.MaxTick(0x2FF, 1));
    }

    [Fact]
    public void PrunedWritesReadAsTheFloor()
    {
        var map = new WriteTickMap();
        map.Assign(0x000, 0x100, 2);
        map.Assign(0x100, 0x100, 6);
        map.Prune(5);

        Assert.Equal(1, map.Count);
        Assert.Equal(2UL, map.Floor);
        Assert.Equal(2UL, map.MaxTick(0x400, 0x100));
        Assert.Equal(6UL, map.MaxTick(0x100, 0x100));
    }

    [Fact]
    public void RepeatedSubrangesOfTheSameTickKeepTheSurroundingRange()
    {
        var map = new WriteTickMap();
        map.Assign(0x100, 0x300, 7);
        map.Assign(0x180, 0x80, 7);
        map.Assign(0x100, 0x100, 7);
        map.Assign(0x300, 0x100, 7);

        Assert.Equal(1, map.Count);
        Assert.Equal(0UL, map.MaxTick(0xFF, 1));
        Assert.Equal(7UL, map.MaxTick(0x100, 0x300));
        Assert.Equal(0UL, map.MaxTick(0x400, 1));
    }

    [Theory]
    [InlineData(3UL)]
    [InlineData(9UL)]
    public void ReplacingAnExactRangeStillMergesBothNeighbors(ulong previousTick)
    {
        var map = new WriteTickMap();
        map.Assign(0x000, 0x100, 7);
        map.Assign(0x100, 0x100, previousTick);
        map.Assign(0x200, 0x100, 7);
        map.Assign(0x100, 0x100, 7);

        Assert.Equal(1, map.Count);
        Assert.Equal(7UL, map.MaxTick(0x000, 0x300));
        Assert.Equal(0UL, map.MaxTick(0x300, 1));
    }

    [Fact]
    public void ContainedWritesWithDifferentTicksStillSplitTheRange()
    {
        var map = new WriteTickMap();
        map.Assign(0, 0x300, 7);
        map.Assign(0x100, 0x100, 3);

        Assert.Equal(3, map.Count);
        Assert.Equal(7UL, map.MaxTick(0, 0x100));
        Assert.Equal(3UL, map.MaxTick(0x100, 0x100));
        Assert.Equal(7UL, map.MaxTick(0x200, 0x100));
    }

    [Theory]
    [InlineData(1234)]
    [InlineData(5678)]
    public void MixedAssignmentsAndPruningMatchPerByteReference(int seed)
    {
        var map = new WriteTickMap();
        var ticks = new ulong[256];
        var written = new bool[ticks.Length];
        var random = new System.Random(seed);
        ulong floor = 0;
        for (var step = 0; step < 3000; step++)
        {
            if (step % 11 == 0)
            {
                var below = (ulong)random.Next(1, 20);
                map.Prune(below);
                for (var index = 0; index < ticks.Length; index++)
                {
                    if (!written[index] || ticks[index] >= below) continue;
                    floor = Math.Max(floor, ticks[index]);
                    written[index] = false;
                }
            }
            else
            {
                var begin = random.Next(ticks.Length);
                var size = random.Next(ticks.Length - begin + 1);
                var tick = (ulong)random.Next(20);
                map.Assign((ulong)begin, (ulong)size, tick);
                Array.Fill(ticks, tick, begin, size);
                Array.Fill(written, true, begin, size);
            }

            Assert.Equal(floor, map.Floor);
            var expectedCount = 0;
            for (var index = 0; index < ticks.Length; index++)
                if (written[index] && (index == 0 || !written[index - 1] || ticks[index] != ticks[index - 1])) expectedCount++;
            Assert.Equal(expectedCount, map.Count);
            var queryBegin = random.Next(ticks.Length);
            var querySize = random.Next(ticks.Length - queryBegin + 1);
            var expected = floor;
            for (var index = queryBegin; index < queryBegin + querySize; index++)
                if (written[index]) expected = Math.Max(expected, ticks[index]);
            Assert.Equal(expected, map.MaxTick((ulong)queryBegin, (ulong)querySize));
        }
    }
}
