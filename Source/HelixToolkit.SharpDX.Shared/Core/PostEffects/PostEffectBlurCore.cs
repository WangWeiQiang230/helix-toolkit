﻿/*
The MIT License (MIT)
Copyright (c) 2018 Helix Toolkit contributors
*/
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

#if !NETFX_CORE
namespace HelixToolkit.Wpf.SharpDX.Core
#else
namespace HelixToolkit.UWP.Core
#endif
{
    using global::SharpDX;
    using Render;
    using Shaders;
    using Utilities;


    /// <summary>
    /// 
    /// </summary>
    public class PostEffectBlurCore : DisposeObject
    {
        /// <summary>
        /// Gets the current ShaderResourceView.
        /// </summary>
        /// <value>
        /// The current SRV.
        /// </value>
        public ShaderResourceView CurrentSRV { get { return renderTargetBlur[0].TextureView; } }

        public int Width { get { return texture2DDesc.Width; } }

        public int Height { get { return texture2DDesc.Height; } }
        /// <summary>
        /// Gets the current RenderTargetView.
        /// </summary>
        /// <value>
        /// The current RTV.
        /// </value>
        public RenderTargetView CurrentRTV { get { return renderTargetBlur[0].RenderTargetView; } }

        private IShaderPass screenBlurPassVertical;

        private IShaderPass screenBlurPassHorizontal;

        #region Texture Resources
        private const int NumPingPongBlurBuffer = 2;

        private ShaderResouceViewProxy[] renderTargetBlur = new ShaderResouceViewProxy[NumPingPongBlurBuffer];

        private int textureSlot;

        private int samplerSlot;

        private SamplerState sampler;

        private Texture2DDescription texture2DDesc = new Texture2DDescription()
        {
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            Usage = ResourceUsage.Default,
            ArraySize = 1,
            MipLevels = 1,
            OptionFlags = ResourceOptionFlags.None,
            SampleDescription = new global::SharpDX.DXGI.SampleDescription(1, 0)
        };

        private ShaderResourceViewDescription targetResourceViewDesc = new ShaderResourceViewDescription()
        {
            Dimension = ShaderResourceViewDimension.Texture2D,
            Texture2D = new ShaderResourceViewDescription.Texture2DResource()
            {
                MipLevels = 1,
                MostDetailedMip = 0,
            }
        };

        private RenderTargetViewDescription renderTargetViewDesc = new RenderTargetViewDescription()
        {
            Dimension = RenderTargetViewDimension.Texture2D,
            Texture2D = new RenderTargetViewDescription.Texture2DResource() { MipSlice = 0 }
        };
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="PostEffectMeshOutlineBlurCore"/> class.
        /// </summary>
        public PostEffectBlurCore(global::SharpDX.DXGI.Format textureFormat,
            IShaderPass blurVerticalPass, IShaderPass blurHorizontalPass, int textureSlot, int samplerSlot, 
            SamplerStateDescription sampler, IEffectsManager manager)
        {
            screenBlurPassVertical = blurVerticalPass;
            screenBlurPassHorizontal = blurHorizontalPass;
            this.textureSlot = textureSlot;
            this.samplerSlot = samplerSlot;
            this.sampler = Collect(manager.StateManager.Register(sampler));
            texture2DDesc.Format = targetResourceViewDesc.Format = renderTargetViewDesc.Format = textureFormat;
        }

        public void Resize(Device device, int width, int height)
        {
            if (texture2DDesc.Width != width || texture2DDesc.Height != height)
            {
                texture2DDesc.Width = width;
                texture2DDesc.Height = height;

                for (int i = 0; i < NumPingPongBlurBuffer; ++i)
                {
                    RemoveAndDispose(ref renderTargetBlur[i]);
                    renderTargetBlur[i] = Collect(new ShaderResouceViewProxy(device, texture2DDesc));
                    renderTargetBlur[i].CreateView(renderTargetViewDesc);
                    renderTargetBlur[i].CreateView(targetResourceViewDesc);
                }
            }
        }

        public virtual void Run(DeviceContextProxy deviceContext, int iteration, int initVerticalIter = 0, int initHorizontalIter = 0)
        {
            deviceContext.DeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            if (!screenBlurPassVertical.IsNULL)
            {
                screenBlurPassVertical.GetShader(ShaderStage.Pixel).BindSampler(deviceContext, samplerSlot, sampler);
                screenBlurPassVertical.BindShader(deviceContext);
                screenBlurPassVertical.BindStates(deviceContext, StateType.BlendState | StateType.RasterState | StateType.DepthStencilState);
                for (int i = initVerticalIter; i < iteration; ++i)
                {
                    SwapTargets();
                    BindTarget(null, renderTargetBlur[0], deviceContext, texture2DDesc.Width, texture2DDesc.Height);
                    screenBlurPassVertical.GetShader(ShaderStage.Pixel).BindTexture(deviceContext, textureSlot, renderTargetBlur[1].TextureView);
                    deviceContext.DeviceContext.Draw(4, 0);
                }
                screenBlurPassVertical.GetShader(ShaderStage.Pixel).BindTexture(deviceContext, textureSlot, null);
            }
          
            if (!screenBlurPassHorizontal.IsNULL)
            {
                screenBlurPassHorizontal.GetShader(ShaderStage.Pixel).BindSampler(deviceContext, samplerSlot, sampler);
                screenBlurPassHorizontal.BindShader(deviceContext);
                screenBlurPassHorizontal.BindStates(deviceContext, StateType.BlendState | StateType.RasterState | StateType.DepthStencilState);
                for (int i = initHorizontalIter; i < iteration; ++i)
                {
                    SwapTargets();
                    BindTarget(null, renderTargetBlur[0], deviceContext, texture2DDesc.Width, texture2DDesc.Height);
                    screenBlurPassHorizontal.GetShader(ShaderStage.Pixel).BindTexture(deviceContext, textureSlot, renderTargetBlur[1].TextureView);
                    deviceContext.DeviceContext.Draw(4, 0);
                }
                screenBlurPassHorizontal.GetShader(ShaderStage.Pixel).BindTexture(deviceContext, textureSlot, null);
            }
        }

        private void SwapTargets()
        {
            //swap buffer
            var current = renderTargetBlur[0];
            renderTargetBlur[0] = renderTargetBlur[1];
            renderTargetBlur[1] = current;
        }

        private static void BindTarget(DepthStencilView dsv, RenderTargetView targetView, DeviceContext context, int width, int height)
        {
            //context.ClearRenderTargetView(targetView, Color.White);
            context.OutputMerger.SetRenderTargets(dsv, new RenderTargetView[] { targetView });
            context.Rasterizer.SetViewport(0, 0, width, height);
            context.Rasterizer.SetScissorRectangle(0, 0, width, height);
        }
    }
}
