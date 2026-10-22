// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Gpu.Scheduling;
using SharpEmu.Libs.VideoOut;
using Silk.NET.Vulkan;

namespace SharpEmu.Libs.Gpu.Buffers;

internal delegate bool GuestBufferSourceReader(ulong address, Span<byte> destination);

// The cache owns the staging ring; temporary sources retire after their submission.
internal sealed class GuestBufferUploader(
    GpuDeviceInfo device, SubmissionScheduler scheduler, ICpuMemory guest, GpuRingBuffer staging)
{
    public GpuBuffer? PrepareSource(ulong bufferAddress, Span<BufferCopy> regions,
        ulong totalSize, ulong requestedAddress, ulong requestedSize, GuestBufferSourceReader? readSource = null) =>
        PrepareSource(bufferAddress, regions, totalSize, requestedAddress, requestedSize, readSource, default, out _);

    // With a snapshot at least totalSize long, the guest bytes are read into it and copied to the source
    // from there, and sourceBase is where the snapshot's first byte lies in the source. The source is
    // write-combining memory, which reads back very slowly; a caller that needs the uploaded bytes again
    // reads the snapshot instead.
    public GpuBuffer? PrepareSource(ulong bufferAddress, Span<BufferCopy> regions,
        ulong totalSize, ulong requestedAddress, ulong requestedSize, GuestBufferSourceReader? readSource,
        Span<byte> snapshot, out ulong sourceBase)
    {
        using var profileScope = RenderPhaseProfile.MeasureDetail(RenderPhaseProfile.Phase.BufferStagingUpload);
        sourceBase = 0;
        if (regions.IsEmpty)
        {
            return null;
        }

        var keep = (ulong)snapshot.Length >= totalSize;
        if (staging.TryMap(totalSize, out var baseOffset, 4))
        {
            foreach (ref var copy in regions)
            {
                var destination = staging.Mapped.Slice((int)(baseOffset + copy.SrcOffset), (int)copy.Size);
                ReadGuestBytes(bufferAddress + copy.DstOffset, keep ? snapshot.Slice((int)copy.SrcOffset, (int)copy.Size) : destination,
                    requestedAddress, requestedSize, readSource);
                if (keep)
                {
                    snapshot.Slice((int)copy.SrcOffset, (int)copy.Size).CopyTo(destination);
                }

                copy.SrcOffset += baseOffset;
            }

            staging.Commit();
            sourceBase = baseOffset;
            return staging;
        }

        var temporary = new GpuBuffer(device, scheduler, GpuBufferUsage.Upload, 0, BufferUsageFlags.TransferSrcBit, totalSize);
        foreach (ref readonly var copy in regions)
        {
            var destination = temporary.Mapped.Slice((int)copy.SrcOffset, (int)copy.Size);
            ReadGuestBytes(bufferAddress + copy.DstOffset, keep ? snapshot.Slice((int)copy.SrcOffset, (int)copy.Size) : destination,
                requestedAddress, requestedSize, readSource);
            if (keep)
            {
                snapshot.Slice((int)copy.SrcOffset, (int)copy.Size).CopyTo(destination);
            }
        }

        temporary.Flush(0, totalSize);
        scheduler.QueueCompletionAction(temporary.Dispose);
        return temporary;
    }

    private void ReadGuestBytes(ulong address, Span<byte> destination, ulong requestedAddress, ulong requestedSize, GuestBufferSourceReader? readSource)
    {
        if (!(readSource is null ? guest.TryRead(address, destination) : readSource(address, destination)))
        {
            Console.Error.WriteLine($"[GPU][READ_FAILURE] addr=0x{address:X16} size=0x{destination.Length:X}");
            Console.Error.WriteLine($"[GPU][READ_FAILURE] binding=0x{requestedAddress:X16} size=0x{requestedSize:X}");
            try
            {
                Console.Error.WriteLine(guest.DescribeReadRange(address, (ulong)destination.Length));
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[GPU][READ_FAILURE] mapping query failed: {error.GetType().Name}");
            }
            throw SubmissionScheduler.Fatal($"Could not read guest memory: addr=0x{address:X16} size=0x{destination.Length:X}");
        }
    }
}
