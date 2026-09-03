Shader "Custom/VolumeFixture"
{
    Properties
    {
        [Header(Color and Transparency)]
        [HDR] _Color ("Tint Color (HDR & Alpha)", Color) = (1, 1, 1, 1)
        [HideInInspector] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Fill ("Fill Opacity (default: 0.02 - 0.1)", Float) = 0.04

        [Header(Fresnel Rim Silhouette)]
        _RimPower ("Rim Power / Sharpness (rec: 1.0 - 8.0)", Float) = 3.0
        _RimBoost ("Rim Boost / Brightness (rec: 0.0 - 5.0)", Float) = 2.0

        [Header(Hexagon Pattern)]
        _HexScale ("Hex Scale / Density (rec: 1.0 - 10.0)", Float) = 3.0
        _HexSpeed ("Hex Crawl Speed (rec: 0.0 - 0.2)", Float) = 0.06
        _HexThickness ("Hex Line Thickness (rec: 0.01 - 0.15)", Float) = 0.05
        _HexIntensity ("Hex Brightness Multiplier (rec: 0.0 - 2.0)", Float) = 0.5
        _HexSpacing ("Hex Cell Spacing Ratio (rec: 0.7 - 0.95)", Float) = 0.85

        [Header(Contact Line)]
        _IntersectWidth ("Line Width (rec: 0.2 - 1.0)", Float) = 0.45
        _IntersectBoost ("Line Brightness (rec: 1 - 5)", Float) = 2.5
        _IntersectPower ("Line Sharpness (rec: 3 - 8)", Float) = 4.0

        [HideInInspector] _SelfPosRad ("Self Pos Rad", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SelfMeta ("Self Meta", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 screenPos    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BaseColor;
                float _Fill;
                float _RimPower;
                float _RimBoost;
                float _HexScale;
                float _HexSpeed;
                float _HexThickness;
                float _HexIntensity;
                float _HexSpacing;
                float _IntersectWidth;
                float _IntersectBoost;
                float _IntersectPower;
                float4 _SelfPosRad;
                float4 _SelfMeta;
            CBUFFER_END

            // Filled by VolumeFixtureRegistry. xyz = centre, w = radius.
            // Meta.x = 0 sphere, 1 Y-aligned capped cylinder; Meta.y = half-height.
            #define FIXTURE_MAX 64
            int _FixtureCount;
            float4 _FixturePosRad[FIXTURE_MAX];
            float4 _FixtureMeta[FIXTURE_MAX];

            // Exact 2D hexagon signed distance function
            float HexDist(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            // Clean isolated regular hexagonal grid with custom spacing and crisp anti-aliased borders
            float HexGridClean(float2 p, float thickness, float cellRatio)
            {
                // Hex tile basis
                const float2 s = float2(1.732050808, 3.0);
                const float2 h = s * 0.5;

                float2 p1 = fmod(p, s);
                if (p1.x < 0.0) p1.x += s.x;
                if (p1.y < 0.0) p1.y += s.y;
                float2 a = p1 - h;

                float2 p2 = fmod(p - h, s);
                if (p2.x < 0.0) p2.x += s.x;
                if (p2.y < 0.0) p2.y += s.y;
                float2 b = p2 - h;

                float2 g = dot(a, a) < dot(b, b) ? a : b;

                // Radius of inner hexagon with spacing
                float hexRadius = 0.5 * cellRatio;
                float d = HexDist(g, hexRadius);

                // Screen/world-space anti-aliasing via derivative length
                float aa = max(fwidth(d), 0.005);
                float halfThick = thickness * 0.5;

                // Draw perimeter wireframe line
                return 1.0 - smoothstep(halfThick - aa, halfThick + aa, abs(d));
            }

            float EvaluateHexTriplanar(float3 worldPos, float3 normalWS, float scale, float speed, float thickness, float cellRatio)
            {
                float3 animPos = (worldPos + float3(speed * 0.5, speed, speed * 0.3) * _Time.y) * scale;

                float3 blendWeights = pow(abs(normalWS), 6.0);
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z + 1e-5);

                float hexX = HexGridClean(animPos.yz, thickness, cellRatio);
                float hexY = HexGridClean(animPos.xz, thickness, cellRatio);
                float hexZ = HexGridClean(animPos.xy, thickness, cellRatio);

                return hexX * blendWeights.x + hexY * blendWeights.y + hexZ * blendWeights.z;
            }

            float SdCappedCylinder(float3 p, float halfH, float r)
            {
                float2 d = abs(float2(length(p.xz), p.y)) - float2(r, halfH);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            float SurfaceDist(float3 p, float4 posRad, float4 meta)
            {
                if (meta.x >= 0.5)
                    return abs(SdCappedCylinder(p - posRad.xyz, max(meta.y, 1e-4), posRad.w));
                return abs(length(p - posRad.xyz) - posRad.w);
            }

            float PeerIntersect(float3 p)
            {
                // No self id uploaded: skip, otherwise sdf-to-self lights the whole shell.
                if (_SelfPosRad.w < 1e-4 || _FixtureCount < 2)
                    return 0.0;

                float width = max(_IntersectWidth, 1e-4);
                float power = max(_IntersectPower, 0.1);
                float best = 0.0;
                int n = min(_FixtureCount, FIXTURE_MAX);
                for (int i = 0; i < FIXTURE_MAX; i++)
                {
                    if (i >= n)
                        break;

                    float3 delta = _SelfPosRad.xyz - _FixturePosRad[i].xyz;
                    if (dot(delta, delta) < 0.0025 &&
                        abs(_SelfPosRad.w - _FixturePosRad[i].w) < 0.05 &&
                        abs(_SelfMeta.x - _FixtureMeta[i].x) < 0.5)
                        continue;

                    float d = SurfaceDist(p, _FixturePosRad[i], _FixtureMeta[i]);
                    float t = 1.0 - saturate(d / width);
                    best = max(best, pow(t, power));
                }
                return best * _IntersectBoost;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalize(normalInput.normalWS);
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 tint = _Color;
                // Script fallback compatibility if _Color is untouched white and _BaseColor is set
                if (tint.r == 1.0 && tint.g == 1.0 && tint.b == 1.0 && tint.a == 1.0 &&
                    (_BaseColor.r != 1.0 || _BaseColor.g != 1.0 || _BaseColor.b != 1.0 || _BaseColor.a != 1.0))
                {
                    tint = _BaseColor;
                }

                float alphaScale = saturate(tint.a);
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                // 1. Fresnel rim silhouette
                float NdotV = abs(dot(normalWS, viewDirWS));
                float fresnel = 1.0 - saturate(NdotV);
                float rim = pow(fresnel, max(_RimPower, 0.01)) * _RimBoost;

                // 2. Procedural triplanar clean hexagons
                float hexPattern = EvaluateHexTriplanar(input.positionWS, normalWS, _HexScale, _HexSpeed, _HexThickness, _HexSpacing);
                float hex = hexPattern * _HexIntensity;

                // 3. Depth intersection
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeDepth = -TransformWorldToView(input.positionWS).z;

                float depthDiff = sceneEyeDepth - surfaceEyeDepth;
                float depthHit = 0.0;
                if (depthDiff >= 0.0 && depthDiff < _IntersectWidth)
                {
                    float t = 1.0 - (depthDiff / max(_IntersectWidth, 1e-4));
                    depthHit = pow(saturate(t), max(_IntersectPower, 0.1)) * _IntersectBoost;
                }

                float intersect = max(depthHit, PeerIntersect(input.positionWS));

                // 4. Fill and combined lighting
                float fill = _Fill;
                float totalIntensity = fill + rim + hex;
                float3 finalRGB = tint.rgb * totalIntensity;

                // Add intersection glow (white-cyan / tint fusion)
                float3 contactColor = lerp(tint.rgb * 2.0, float3(0.85, 1.0, 1.0) * 3.0, 0.35);
                finalRGB += contactColor * intersect;

                // Multiply by alphaScale so the alpha slider directly modulates the entire brightness / opacity
                finalRGB *= alphaScale;

                // Blend One OneMinusSrcAlpha (premultiplied):
                // RGB contributes finalRGB directly, Alpha determines background occlusion
                float totalAlpha = saturate((fill * 1.5 + rim * 0.7 + hex * 0.5 + intersect) * alphaScale);

                return float4(finalRGB, totalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

