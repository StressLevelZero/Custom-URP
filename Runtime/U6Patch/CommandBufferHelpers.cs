using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class CommandBufferHelpers
{
    public static RasterCommandBuffer GetRasterCommandBuffer(CommandBuffer cmd)
    {
        return (RasterCommandBuffer)cmd;
    }
}
