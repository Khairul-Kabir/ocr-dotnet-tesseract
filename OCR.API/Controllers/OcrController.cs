using Microsoft.AspNetCore.Mvc;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tesseract;

namespace OCR.API.Controllers
{
    [Route("api/ocr")]
    [ApiController]
    public class OcrController : ControllerBase
    {
        [HttpPost]
        [Route("extract")]
        public async Task<IActionResult> ExtractOcr([FromBody] OcrRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ImageBase64_1) || string.IsNullOrEmpty(request.ImageBase64_2))
                return BadRequest(new { message = "Invalid request. Both images must be provided in Base64 format." });

            try
            {
                // Convert Base64 to Bitmap
                Bitmap image1 = Base64ToBitmap(request.ImageBase64_1);
                Bitmap image2 = Base64ToBitmap(request.ImageBase64_2);

                // Perform OCR
                string text1 = ExtractTextFromImage(image1);
                string text2 = ExtractTextFromImage(image2);

                return Ok(new
                {
                    Image1_OcrText = text1,
                    Image2_OcrText = text2
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing the images.", error = ex.Message });
            }
        }

        private static Bitmap Base64ToBitmap(string base64String)
        {
            byte[] imageBytes = Convert.FromBase64String(base64String);
            using (MemoryStream ms = new MemoryStream(imageBytes))
            {
                using (var tempBitmap = new Bitmap(ms))
                {
                    // Create a new bitmap to avoid GDI+ errors
                    return new Bitmap(tempBitmap);
                }
            }
        }

        private static string ExtractTextFromImage(Bitmap image)
        {
            string tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata-main");

            using (var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    try
                    {
                        // Ensure the image is not locked before saving
                        Bitmap newImage = new Bitmap(image);
                        newImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        newImage.Dispose(); // Explicitly dispose of the copied image
                    }
                    catch (ExternalException ex)
                    {
                        throw new InvalidOperationException("Failed to save the image to the memory stream.", ex);
                    }

                    byte[] imageBytes = ms.ToArray();

                    using (var pix = Pix.LoadFromMemory(imageBytes))
                    {
                        using (var page = engine.Process(pix))
                        {
                            return page.GetText().Trim();
                        }
                    }
                }
            }
        }

    }

    public class OcrRequest
    {
        public string ImageBase64_1 { get; set; }
        public string ImageBase64_2 { get; set; }
    }
}
