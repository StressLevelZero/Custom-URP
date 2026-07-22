#ifndef FIXED_POINT_MULTIPLIER
#define FIXED_POINT_MULTIPLIER

#define FIXED_POINT_MAX min16float(1.0)
#define INV_FIXED_POINT_MAX min16float(1.0)

min16float4 EncodeFixedPointColor(min16float4 colorIn)
{
    return colorIn;
    //return min16float4(INV_FIXED_POINT_MAX * sqrt(colorIn.rgb), INV_FIXED_POINT_MAX * colorIn.a);
}

min16float4 DecodeFixedPointColor(min16float4 colorIn)
{
    return colorIn;
    //min16float4 colorOut = colorIn * FIXED_POINT_MAX;
    //return min16float4(colorOut.rgb * colorOut.rgb, colorOut.a);
}

#endif