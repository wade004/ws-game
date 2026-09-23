// VfxAdditiveUnlit：ADR-0074 新增，供 EffectSequencePlayer 的 additive 混合模式使用（见该类型
// GetAdditiveMaterial 判断记录）。逐字复用 URP 内置 "Universal Render Pipeline/2D/
// Sprite-Unlit-Default"（Packages/com.unity.render-pipelines.universal/Shaders/2D/
// Sprite-Unlit-Default.shader）的顶点/片元管线（COMMON_2D_INPUTS/OUTPUTS 宏已正确处理 SpriteRenderer
// 的 UV/顶点色/GPU Instancing/像素对齐，是本仓库唯一已验证在本项目 URP 版本下正确渲染 2D 精灵的
// 最小管线），唯一改动是 Blend 状态：alpha 混合（SrcAlpha OneMinusSrcAlpha）换成 additive
// （One One，叠加变亮），不新增任何美术属性——本适配层不产出正式美术资源（同占位方块/占位影子一贯
// 定位，见 UnityRenderer2D.cs 顶部注释）。
Shader "GameFoundation/Vfx/AdditiveUnlit"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // 与 Sprite-Unlit-Default.shader 唯一的差异点：additive（叠加变亮），不做 alpha 覆盖。
        Blend One One
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }
}
