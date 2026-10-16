#if USEVELDRID
using System;
using Veldrid;
using Veldrid.StartupUtilities;
using Engine.Renderer;

namespace Engine.Renderer.Veldrid
{
    public class VeldridBackend : IRenderBackend, IDisposable
    {
        private GraphicsDevice _gd;
        private Sdl2Window _window;

        public void Initialize()
        {
            var windowCI = new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "Engine Sample");
            _window = VeldridStartup.CreateWindow(ref windowCI);
            var options = new GraphicsDeviceOptions(false, null, false, ResourceBindingModel.Improved);
            _gd = VeldridStartup.CreateGraphicsDevice(_window, options);
        }

        public void Render(SceneGraph scene, Camera camera)
        {
            if (_gd == null) return;
            var cl = _gd.ResourceFactory.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(_gd.SwapchainFramebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.End();
            _gd.SubmitCommands(cl);
            _gd.SwapBuffers();
            cl.Dispose();
        }

        public void Shutdown()
        {
            _gd?.Dispose();
            _gd = null;
            _window?.Dispose();
            _window = null;
        }

        public void Dispose() => Shutdown();
    }
}
#endif