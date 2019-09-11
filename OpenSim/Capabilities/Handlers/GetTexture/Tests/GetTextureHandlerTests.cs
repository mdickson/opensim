
/*
namespace OpenSim.Capabilities.Handlers.GetTexture.Tests
{
    [TestFixture]
    public class GetTextureHandlerTests : OpenSimTestCase
    {
        [Test]
        public void TestTextureNotFound()
        {
            TestHelpers.InMethod();

            // Overkill - we only really need the asset service, not a whole scene.
            Scene scene = new SceneHelpers().SetupScene();

            GetTextureHandler handler = new GetTextureHandler("/gettexture", scene.AssetService, "TestGetTexture", null, null);
            TestOSHttpRequest req = new TestOSHttpRequest();
            TestOSHttpResponse resp = new TestOSHttpResponse();
            req.Url = new Uri("http://localhost/?texture_id=00000000-0000-1111-9999-000000000012");
            handler.Handle(null, null, req, resp);
            Assert.That(resp.StatusCode, Is.EqualTo((int)System.Net.HttpStatusCode.NotFound));
        }
    }
}
*/
