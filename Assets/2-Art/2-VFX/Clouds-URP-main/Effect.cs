//Created by Paro.
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine;

public class Effect : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        //future settings
        public Material material;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        public Color color = new Color(1,1,1,1);
    }

    public Settings settings = new Settings();
    class Pass : ScriptableRenderPass
    {
        public Settings settings;
#if UNITY_2022_1_OR_NEWER
        private RTHandle source;
        private RTHandle tempTexture;
#else
        private RenderTargetIdentifier source;
        private RenderTargetHandle tempTexture;
#endif

        private string profilerTag;

#if UNITY_2022_1_OR_NEWER
        public void Setup(RTHandle source)
        {
            this.source = source;
        }
#else
        public void Setup(RenderTargetIdentifier source)
        {
            this.source = source;
        }
#endif

        public Pass(string profilerTag)
        {
            this.profilerTag = profilerTag;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
#if UNITY_2022_1_OR_NEWER
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_EffectTempTexture");
            ConfigureTarget(tempTexture);
#else
            cmd.GetTemporaryRT(tempTexture.id, cameraTextureDescriptor);
            ConfigureTarget(tempTexture.Identifier());
#endif
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
            cmd.Clear();

            //it is very important that if something fails our code still calls 
            //CommandBufferPool.Release(cmd) or we will have a HUGE memory leak
            if(settings.material == null)
            {
                CommandBufferPool.Release(cmd);
                return;
            }

            try
            {
                //here we set out material properties
                //...
                settings.material.SetColor("_color", settings.color);

                //never use a Blit from source to source, as it only works with MSAA
                // enabled and the scene view doesnt have MSAA,
                // so the scene view will be pure black

#if UNITY_2022_1_OR_NEWER
                Blitter.BlitCameraTexture(cmd, source, tempTexture);
                Blitter.BlitCameraTexture(cmd, tempTexture, source, settings.material, 0);
#else
                cmd.Blit(source, tempTexture.Identifier());
                cmd.Blit(tempTexture.Identifier(), source, settings.material, 0);
#endif

                context.ExecuteCommandBuffer(cmd);
            }
            catch
            {
                Debug.LogError("Error");
            }
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
#if UNITY_2022_1_OR_NEWER
            tempTexture?.Release();
#endif
        }
    }

    Pass pass;
    public override void Create()
    {
        pass = new Pass("Effect");
        name = "Effect";
        pass.settings = settings;
        pass.renderPassEvent = settings.renderPassEvent;
    }
#if UNITY_2022_1_OR_NEWER
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        var cameraColorTargetIdent = renderer.cameraColorTargetHandle;
        pass.Setup(cameraColorTargetIdent);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }
#else
    // called every frame once per camera
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraColorTargetIdent = renderer.cameraColorTarget;
        pass.Setup(cameraColorTargetIdent);
        renderer.EnqueuePass(pass);
    }
#endif

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}

