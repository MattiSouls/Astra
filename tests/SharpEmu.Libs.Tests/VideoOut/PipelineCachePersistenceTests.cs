// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class PipelineCachePersistenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("file")]
    public void KeepsTheCacheFileByDefaultOnEveryHost(string? cacheMode)
    {
        Assert.True(VulkanVideoPresenter.PersistsPipelineCache(cacheMode));
    }

    [Fact]
    public void ZeroKeepsTheCacheInMemoryOnly()
    {
        Assert.False(VulkanVideoPresenter.PersistsPipelineCache("0"));
    }
}
