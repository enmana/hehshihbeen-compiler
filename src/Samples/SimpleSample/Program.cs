using Engine.Content;
using Engine.Renderer;
using Engine.Scene;
// using Engine.Renderer.Veldrid; // Veldrid backend placeholder

namespace Samples.SimpleSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var scene = new SceneGraph();
            var node = new Node("Cube");
            scene.Root.AddChild(node);

            var mesh = new Mesh();
            // minimal mesh; leave empty

            var rm = new ResourceManager();
            var handle = rm.LoadMeshAsync("cube.obj").Result;

            IRenderBackend backend = new Engine.Renderer.Veldrid.VeldridBackend(); // explicit type reference to placeholder backend
            backend.Initialize();
            var cam = new Camera();
            backend.Render(scene, cam);
            backend.Shutdown();

            Console.WriteLine("Sample ran (no real rendering).\n");
        }
    }
}
