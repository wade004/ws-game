// ShowcaseModelLit：3D 演示场景（ADR-0158）的角色着色器。
//
// 判断记录（为什么不用 URP/Lit）：本工程的渲染管线是 URP 的 2D Renderer，它只绘制 LightMode 为 Universal2D / SRPDefaultUnlit 的 Pass，
// 且不向 3D 着色器提供主光源数据，URP/Lit 在这里会画成粉红或全黑。所以本着色器自己带两个 Pass（Universal2D 与 UniversalForward，同一份程序），
// 光照用写死在着色器里的世界空间"太阳方向"做半兰伯特明暗，不依赖任何场景光源。
// 判断记录（flash_intensity）：框架的 3D 渲染器经 MaterialPropertyBlock 向实例下全部渲染器广播命名参数 flash_intensity / fade_alpha
// （IRenderer3D.SetMaterialParam，参数含义由游戏侧着色器决定）；本着色器声明 flash_intensity（受击闪白，0 = 原色；与精灵管线同义：颜色过曝到 (1 + 强度) 倍，保留明暗与贴图细节，
// 不是整块填成白色——精灵的 flash_intensity 取 1 时是 RGB 乘 2，这里同样），
// 放在 UnityPerMaterial 常量缓冲里，这样属性块覆盖对批处理路径与普通路径都生效。fade_alpha 不声明（实验室里尸体由清场移除）。
Shader "GameFoundation/Showcase/ModelLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        flash_intensity("Flash Intensity", Float) = 0
        _Ambient("Ambient", Range(0, 1)) = 0.52
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half flash_intensity;
            half _Ambient;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
        };

        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.normalWS = (half3)normalize(TransformObjectToWorldNormal(input.normalOS));
            return output;
        }

        half4 frag(Varyings input) : SV_Target
        {
            half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            // 写死的太阳方向（指向光源）：来自镜头一侧偏左偏上——镜头在世界 -Z 一侧看向 +Z，朝向镜头的表面法线是 -Z。
            half3 sun = normalize(half3(-0.35h, 0.45h, -0.82h));
            half ndl = dot(normalize(input.normalWS), sun) * 0.5h + 0.5h;
            half light = lerp(_Ambient, 1.08h, ndl * ndl);
            half3 color = albedo.rgb * light;
            color = color * (1.0h + max(flash_intensity, 0.0h));
            return half4(color, 1);
        }
        ENDHLSL

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
