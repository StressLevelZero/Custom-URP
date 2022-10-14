float4 _TileRadii;
float4 _EyeCenterCoords;

void GetTileParams(float2 screenUV, out float4 radii, out bool inner, out bool middle, out bool outer, out bool grid)
{
	//float xCenter = (UNITY_MATRIX_P._m03 - UNITY_MATRIX_P._m02) / (UNITY_MATRIX_P._m33 - UNITY_MATRIX_P._m32);
	//xCenter = 0.5 * xCenter + 0.5;

	uint2 gridUnit = floor(screenUV * 0.5);
	float2 gridUnit2 = 4 * floor(screenUV * 0.25);
	float2 center = unity_StereoEyeIndex == 0 ? _EyeCenterCoords.xy : _EyeCenterCoords.zw;
	center *= _ScreenParams.xy;
	radii.x = length(gridUnit2 + float2(0.0, 0.0) - center) / _ScreenParams.y;
	radii.y = length(gridUnit2 + float2(4.0, 0.0) - center) / _ScreenParams.y;
	radii.z = length(gridUnit2 + float2(4.0, 4.0) - center) / _ScreenParams.y;
	radii.w = length(gridUnit2 + float2(0.0, 4.0) - center) / _ScreenParams.y;
	inner = any(radii < _TileRadii.x);
	middle = any(radii < _TileRadii.y) && any(radii >= _TileRadii.x);
	outer = any(radii >= _TileRadii.y);

	bool grid1 = !((gridUnit.x & 1) ^ (gridUnit.y & 1));
	bool grid2 = (gridUnit.x & 1) & (gridUnit.y & 1);
	//float avgRadius = (radii.x + radii.y + radii.z + radii.w) * 0.25;
	grid = middle ? grid1 : true;
	grid = outer && !middle ? grid2 : grid;
}

bool IsTileCovered(float2 screenUV)
{
	//float xCenter = (UNITY_MATRIX_P._m03 - UNITY_MATRIX_P._m02) / (UNITY_MATRIX_P._m33 - UNITY_MATRIX_P._m32);
	//xCenter = 0.5 * xCenter + 0.5;

	uint2 gridUnit = floor(screenUV * 0.5);
	float2 gridUnit2 = 4 * floor(screenUV * 0.25);
	float2 center = unity_StereoEyeIndex == 0 ? _EyeCenterCoords.xy : _EyeCenterCoords.zw;
	float4 radii;
	radii.x = length(gridUnit2 + float2(0.0, 0.0) - center) / _ScreenParams.y;
	radii.y = length(gridUnit2 + float2(4.0, 0.0) - center) / _ScreenParams.y;
	radii.z = length(gridUnit2 + float2(4.0, 4.0) - center) / _ScreenParams.y;
	radii.w = length(gridUnit2 + float2(0.0, 4.0) - center) / _ScreenParams.y;
	bool inner = any(radii < _TileRadii.x);
	bool middle = any(radii < _TileRadii.y) && any(radii >= _TileRadii.x);
	//outer = any(radii >= _TileRadii.y);

	bool grid1 = !((gridUnit.x & 1) ^ (gridUnit.y & 1));
	bool grid2 = (gridUnit.x & 1) & (gridUnit.y & 1);
	//float avgRadius = (radii.x + radii.y + radii.z + radii.w) * 0.25;
	bool grid = middle ? grid1 : grid2;
	grid = inner ? true : grid;
	
	return grid;
}