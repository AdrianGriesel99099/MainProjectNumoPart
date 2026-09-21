using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // A file whose bytes aren't actually a decodable image slips past the upload form's
    // content-type/size checks (Pages/Upload.cshtml.cs only trusts the browser-supplied
    // Content-Type header and file size — neither proves the bytes decode). Before the fix this
    // covers, Image.LoadAsync inside ThumbnailGenerator threw SixLabors.ImageSharp.
    // UnknownImageFormatException straight out of OnPostAsync as an unhandled 500 instead of a
    // validation message.
    public class UploadModelTests
    {
        private static readonly byte[] NotAnImage = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        private static async Task<byte[]> CreateValidJpegBytesAsync()
        {
            using var image = new Image<Rgba32>(20, 20, new Rgba32(10, 20, 30, 255));
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
            return stream.ToArray();
        }

        private static UploadModel BuildModel(out AppDbContextScope scope)
        {
            var provider = TestIdentityFactory.Build();
            var serviceScope = provider.CreateScope();
            var db = serviceScope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            var userManager = serviceScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            var storage = new FakePhotoStorage();
            var model = new UploadModel(db, new VehicleLookupService(db), new PhotoSequenceAllocator(db),
                storage, userManager)
            {
                Vin = "VIN-UPLOAD-1",
                Stage = Stage.Checkin
            };

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-1") }, "TestAuth"));
            model.PageContext = new PageContext { HttpContext = httpContext };

            scope = new AppDbContextScope(provider, serviceScope, db, storage);
            return model;
        }

        // Bundles what a test needs to assert on afterwards; disposal isn't required since these
        // are in-memory SQLite connections scoped to the test method.
        private sealed class AppDbContextScope
        {
            public AppDbContextScope(ServiceProvider provider, IServiceScope scope, Data.AppDbContext db, FakePhotoStorage storage)
            {
                Provider = provider;
                Scope = scope;
                Db = db;
                Storage = storage;
            }

            public ServiceProvider Provider { get; }
            public IServiceScope Scope { get; }
            public Data.AppDbContext Db { get; }
            public FakePhotoStorage Storage { get; }
        }

        [Fact]
        public async Task OnPostAsync_FileThatIsNotADecodableImage_ReturnsFriendlyErrorInsteadOfThrowing()
        {
            var model = BuildModel(out var scope);
            model.Files = new List<IFormFile> { new FakeFormFile(NotAnImage, "totallyvalid.jpg", "image/jpeg") };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.NotNull(model.ErrorMessage);
            Assert.Contains("totallyvalid.jpg", model.ErrorMessage);
            Assert.Empty(scope.Db.Photos);
        }

        [Fact]
        public async Task OnPostAsync_FileThatIsNotADecodableImage_LeavesNoOrphanedBlobs()
        {
            var model = BuildModel(out var scope);
            model.Files = new List<IFormFile> { new FakeFormFile(NotAnImage, "bad.jpg", "image/jpeg") };

            await model.OnPostAsync();

            Assert.Empty(scope.Storage.Originals);
            Assert.Empty(scope.Storage.Thumbnails);
        }

        [Fact]
        public async Task OnPostAsync_SecondFileInBatchIsNotADecodableImage_CleansUpFirstFilesBlobsToo()
        {
            var model = BuildModel(out var scope);
            var goodBytes = await CreateValidJpegBytesAsync();
            model.Files = new List<IFormFile>
            {
                new FakeFormFile(goodBytes, "good.jpg", "image/jpeg"),
                new FakeFormFile(NotAnImage, "bad.jpg", "image/jpeg")
            };

            var result = await model.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.Empty(scope.Db.Photos);
            // The first file's original+thumbnail were uploaded before the second file's bytes
            // failed to decode — cleanup must remove those too, not just skip the failed one.
            Assert.Empty(scope.Storage.Originals);
            Assert.Empty(scope.Storage.Thumbnails);
        }

        [Fact]
        public async Task OnPostAsync_ValidImage_StillUploadsSuccessfully()
        {
            var model = BuildModel(out var scope);
            var goodBytes = await CreateValidJpegBytesAsync();
            model.Files = new List<IFormFile> { new FakeFormFile(goodBytes, "good.jpg", "image/jpeg") };

            var result = await model.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Single(scope.Db.Photos);
            Assert.Single(scope.Storage.Originals);
            Assert.Single(scope.Storage.Thumbnails);
        }
    }
}
