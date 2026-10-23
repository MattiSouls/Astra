// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.Libs.Gpu.Buffers;

// Counts the points where a GPU command may first depend on guest CPU writes: a command
// stream starting (or resuming) and a wait on guest memory. Commands between two points can
// rely only on writes made before the first, so device-address uploads need one sweep per point.
// The points are counted when the stream is parsed, before its work runs.
public static class GuestOrderingPoints
{
    private static long _generation;

    public static long Generation => Volatile.Read(ref _generation);

    public static void Advance() => Interlocked.Increment(ref _generation);
}
