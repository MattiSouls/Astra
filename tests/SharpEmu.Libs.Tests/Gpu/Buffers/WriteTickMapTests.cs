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

    [Fact]
    public void PartialOverwritesKeepTheTailWhenTheIntervalCountGrowsAndShrinks()
    {
        var map = new WriteTickMap();
        for (ulong index = 0; index < 128; index++) map.Assign(index * 32, 16, index + 1);
        map.Assign(4, 8, 1000);
        Assert.Equal(130, map.Count);
        Assert.Equal(1UL, map.MaxTick(0, 4));
        Assert.Equal(1000UL, map.MaxTick(4, 8));
        Assert.Equal(1UL, map.MaxTick(12, 4));

        map.Assign(4, 40, 2000);
        Assert.Equal(129, map.Count);
        Assert.Equal(2000UL, map.MaxTick(4, 40));
        Assert.Equal(2UL, map.MaxTick(44, 4));
        map.Assign(0, 80, 3);
        Assert.Equal(126, map.Count);
        for (ulong index = 3; index < 128; index++)
            Assert.Equal(index + 1, map.MaxTick(index * 32, 16));
    }

    [Theory]
    [InlineData(7411)]
    [InlineData(8329)]
    public void SparseHighAddressesAndSmallOverwritesMatchPerByteReference(int seed)
    {
        const ulong start = ulong.MaxValue - 4096;
        var map = new WriteTickMap();
        var ticks = new ulong[1024];
        var written = new bool[ticks.Length];
        var random = new System.Random(seed);
        ulong floor = 0;
        for (var step = 0; step < 7000; step++)
        {
            if (step % 127 == 0)
            {
                var below = (ulong)random.Next(32);
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
                var size = random.Next(Math.Min(step % 53 == 0 ? 160 : 17, ticks.Length - begin) + 1);
                var tick = (ulong)random.Next(32);
                map.Assign(start + (ulong)begin, (ulong)size, tick);
                Array.Fill(ticks, tick, begin, size);
                Array.Fill(written, true, begin, size);
            }

            var count = 0;
            for (var index = 0; index < ticks.Length; index++)
                if (written[index] && (index == 0 || !written[index - 1] || ticks[index] != ticks[index - 1])) count++;
            Assert.Equal(count, map.Count);
            Assert.Equal(floor, map.Floor);
            var queryBegin = random.Next(ticks.Length);
            var querySize = random.Next(ticks.Length - queryBegin + 1);
            var expected = floor;
            for (var index = queryBegin; index < queryBegin + querySize; index++)
                if (written[index]) expected = Math.Max(expected, ticks[index]);
            Assert.Equal(expected, map.MaxTick(start + (ulong)queryBegin, (ulong)querySize));
        }
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

    [Fact]
    public void RepeatedSplitsMergesAndPrunesReuseTheMapsBlocks()
    {
        const ulong addressBase = 0x10000;
        var map = new WriteTickMap();
        ulong tick = 0;

        // Counts after each phase; asserted outside the measured rounds, since asserting allocates.
        Span<int> counts = stackalloc int[3];
        void Cycle(Span<int> counts)
        {
            // Fragments that split into several blocks, one write that collapses them, then a prune
            // that empties the map.
            for (var index = 0; index < 1000; index++)
                map.Assign(addressBase + (ulong)index * 8, 4, ++tick);
            counts[0] = map.Count;
            map.Assign(addressBase, 1000 * 8, ++tick);
            counts[1] = map.Count;
            map.Prune(tick + 1);
            counts[2] = map.Count;
        }

        Cycle(counts);
        Assert.Equal([1000, 1, 0], counts.ToArray());
        Assert.Equal(tick, map.Floor);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 20; round++) Cycle(counts);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal([1000, 1, 0], counts.ToArray());
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FragmentedIntervalsRemainOrderedAcrossBlockSplicesAndPruning()
    {
        const int addressSpaceSize = 4096;
        const ulong addressBase = 0x10000;
        var map = new WriteTickMap();
        var ticks = new ulong[addressSpaceSize];
        var written = new bool[addressSpaceSize];
        ulong floor = 0;

        void Assign(int begin, int size, ulong tick)
        {
            map.Assign(addressBase + (ulong)begin, (ulong)size, tick);
            Array.Fill(ticks, tick, begin, size);
            Array.Fill(written, true, begin, size);
        }

        void AssertMatchesReference()
        {
            var expectedCount = 0;
            for (var index = 0; index < addressSpaceSize; index++)
            {
                if (written[index] && (index == 0 || !written[index - 1] || ticks[index] != ticks[index - 1]))
                    expectedCount++;
            }

            Assert.Equal(expectedCount, map.Count);
            Assert.Equal(floor, map.Floor);
            for (var begin = 0; begin < addressSpaceSize; begin += 37)
            {
                var size = Math.Min(43, addressSpaceSize - begin);
                var expected = floor;
                for (var index = begin; index < begin + size; index++)
                    if (written[index]) expected = Math.Max(expected, ticks[index]);
                Assert.Equal(expected, map.MaxTick(addressBase + (ulong)begin, (ulong)size));
            }
        }

        for (var index = 0; index < 400; index++)
            Assign(index * 8, 4, (ulong)index + 1);
        AssertMatchesReference();

        // This replaces intervals across several blocks and preserves partial edge intervals.
        Assign(12 * 8 + 2, (380 * 8 + 2) - (12 * 8 + 2), 10_000);
        AssertMatchesReference();

        map.Prune(10_000);
        for (var index = 0; index < addressSpaceSize; index++)
        {
            if (!written[index] || ticks[index] >= 10_000) continue;
            floor = Math.Max(floor, ticks[index]);
            written[index] = false;
        }
        AssertMatchesReference();

        // Re-fragment the surviving range, then overwrite through multiple block boundaries again.
        for (var index = 0; index < 260; index++)
            Assign(index * 12, 5, (ulong)index + 20_000);
        AssertMatchesReference();

        Assign(140 * 12 + 1, (220 * 12 + 4) - (140 * 12 + 1), 40_000);
        AssertMatchesReference();
    }
}
