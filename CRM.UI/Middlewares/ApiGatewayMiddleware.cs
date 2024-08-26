using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.UI.Middlewares;

public class ApiGatewayMiddleware
{
    private readonly RequestDelegate _next;

    public ApiGatewayMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<ApiGatewayMiddleware> logger, IHttpClientFactory httpClientFactory)
    {
        var request = context.Request;

        var fullRequestPath = request.Path.Value;
        if (fullRequestPath.Contains("api/"))
        {
            const string apiAddress = "https://localhost:3001";
            var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(apiAddress);

            var requestPath = apiAddress + fullRequestPath + request.QueryString;
            var newRequest = new HttpRequestMessage(new HttpMethod(request.Method), requestPath);

            if (request.ContentLength.HasValue && request.ContentLength != 0)
            {
                var sendContent = await GetRequestContentAsync(request);
                newRequest.Content = sendContent;
            }

            var response = await client.SendAsync(newRequest);

            logger.LogInformation($"Запрос {fullRequestPath} ушёл");
            await ConfigureResponseAsync(context, response, logger);
            return;
        }

        await _next(context);
    }

    private static async Task ConfigureResponseAsync(HttpContext context, HttpResponseMessage response, ILogger<ApiGatewayMiddleware> logger)
    {
        context.Response.StatusCode = (int)response.StatusCode;
        if (context.Response.StatusCode == 204)
            return;

        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
        if (response.Content.Headers.ContentDisposition != null)
        {
            await ConfigureResponseWithAttachmentsAsync(context, response);
            logger.LogInformation($"Запрос {context.Request} пришёл со статусом {response.StatusCode}");
            return;
        }

        var content = await response.Content.ReadAsStringAsync();
        await context.Response.WriteAsync(content);
    }

    private static async Task ConfigureResponseWithAttachmentsAsync(HttpContext context, HttpResponseMessage response)
    {
        context.Response.Headers.Add("Content-Disposition", response.Content.Headers.ContentDisposition.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        context.Response.Headers.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
    }

    private static async Task<HttpContent> GetRequestContentAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
            return await GetRequestFormContentAsync(request);

        string requestContent;
        await using (var receiveStream = request.Body)
        {
            using var readStream = new StreamReader(receiveStream, Encoding.UTF8);
            requestContent = await readStream.ReadToEndAsync();
        }

        var mimeType = request.ContentType.Split(";").FirstOrDefault(part => part.Contains("/"));
        var sendContent = new StringContent(requestContent, Encoding.UTF8, mimeType);
        return sendContent;
    }

    private static async Task<MultipartFormDataContent> GetRequestFormContentAsync(HttpRequest request)
    {
        var formData = new MultipartFormDataContent();
        var form = await request.ReadFormAsync();
        foreach (var file in form.Files)
            await AddFileToFormAsync(file, formData);

        foreach (var keyValue in request.Form)
            formData.Add(new StringContent(keyValue.Value, Encoding.UTF8), keyValue.Key);

        return formData;
    }

    private static async Task AddFileToFormAsync(IFormFile file, MultipartFormDataContent formData)
    {
        var byteArray = await ToByteArrayAsync(file);
        var fileContent = new ByteArrayContent(byteArray);
        fileContent.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(file.ContentDisposition);
        fileContent.Headers.ContentDisposition.FileNameStar = fileContent.Headers.ContentDisposition.FileName;
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        formData.Add(fileContent);
    }

    private static async Task<byte[]> ToByteArrayAsync(IFormFile file)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var byteArray = ms.ToArray();
        return byteArray;
    }
}