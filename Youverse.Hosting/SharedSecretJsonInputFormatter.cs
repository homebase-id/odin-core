#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting;

public class SharedSecretJsonInputFormatter : TextInputFormatter, IInputFormatterExceptionPolicy
{
    private readonly JsonOptions _jsonOptions;
    private readonly ILogger<SharedSecretJsonInputFormatter> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SharedSecretJsonInputFormatter"/>.
    /// </summary>
    /// <param name="options">The <see cref="JsonOptions"/>.</param>
    /// <param name="logger">The <see cref="ILogger"/>.</param>
    public SharedSecretJsonInputFormatter(
        JsonOptions options)
    {
        SerializerOptions = options.JsonSerializerOptions;
        _jsonOptions = options;

        SupportedEncodings.Add(UTF8EncodingWithoutBOM);
        SupportedEncodings.Add(UTF16EncodingLittleEndian);

        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> used to configure the <see cref="JsonSerializer"/>.
    /// </summary>
    /// <remarks>
    /// A single instance of <see cref="SystemTextJsonInputFormatter"/> is used for all JSON formatting. Any
    /// changes to the options will affect all input formatting.
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; }

    /// <inheritdoc />
    InputFormatterExceptionPolicy IInputFormatterExceptionPolicy.ExceptionPolicy => InputFormatterExceptionPolicy.MalformedInputExceptions;

    /// <inheritdoc />
    public sealed override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (encoding == null)
        {
            throw new ArgumentNullException(nameof(encoding));
        }

        var httpContext = context.HttpContext;
        var (inputStream, usesTranscodingStream) = GetInputStream(httpContext, encoding);

        object? model = null;
        try
        {
            //TODO: what happens for anonymous requests that don't have a shared secret?  maybe we treat those as normal?
            var encryptedRequest = await JsonSerializer.DeserializeAsync<SharedSecretEncryptedPayload>(inputStream, SerializerOptions);

            if (null == encryptedRequest)
            {
                throw new YouverseException("Failed to deserialize SharedSecretEncryptedRequest");
            }

            var accessor = context.HttpContext.RequestServices.GetRequiredService<DotYouContextAccessor>();
            //hack
            var key = accessor.GetCurrent()?.PermissionsContext?.SharedSecretKey ?? Guid.Empty.ToByteArray().ToSensitiveByteArray();

            var encryptedBytes = Convert.FromBase64String(encryptedRequest.Data);
            var jsonBytes = AesCbc.Decrypt(encryptedBytes, ref key, encryptedRequest.Iv);

            //update the body with the decrypted json file so it can be read by the web api controller
            context.HttpContext.Request.Body = new MemoryStream(jsonBytes);

            model = await JsonSerializer.DeserializeAsync(new MemoryStream(jsonBytes), context.ModelType, SerializerOptions);
            // model = JsonSerializer.Deserialize(json, context.ModelType, SerializerOptions);
        }
        catch (JsonException jsonException)
        {
            var path = jsonException.Path ?? string.Empty;

            var modelStateException = WrapExceptionForModelState(jsonException);

            context.ModelState.TryAddModelError(path, modelStateException, context.Metadata);
            // Log.JsonInputException(_logger, jsonException);
            return InputFormatterResult.Failure();
        }
        catch (Exception exception) when (exception is FormatException || exception is OverflowException)
        {
            // The code in System.Text.Json never throws these exceptions. However a custom converter could produce these errors for instance when
            // parsing a value. These error messages are considered safe to report to users using ModelState.

            context.ModelState.TryAddModelError(string.Empty, exception, context.Metadata);
            // Log.JsonInputException(_logger, exception);

            return InputFormatterResult.Failure();
        }
        finally
        {
            if (usesTranscodingStream)
            {
                await inputStream.DisposeAsync();
            }
        }

        if (model == null && !context.TreatEmptyInputAsDefaultValue)
        {
            // Some nonempty inputs might deserialize as null, for example whitespace,
            // or the JSON-encoded value "null". The upstream BodyModelBinder needs to
            // be notified that we don't regard this as a real input so it can register
            // a model binding error.
            return InputFormatterResult.NoValue();
        }
        else
        {
            // Log.JsonInputSuccess(_logger, context.ModelType);
            return InputFormatterResult.Success(model);
        }
    }

    private Exception WrapExceptionForModelState(JsonException jsonException)
    {
        if (!_jsonOptions.AllowInputFormatterExceptionMessages)
        {
            // This app is not opted-in to System.Text.Json messages, return the original exception.
            return jsonException;
        }

        // InputFormatterException specifies that the message is safe to return to a client, it will
        // be added to model state.
        return new InputFormatterException(jsonException.Message, jsonException);
    }

    private static (Stream inputStream, bool usesTranscodingStream) GetInputStream(HttpContext httpContext, Encoding encoding)
    {
        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            return (httpContext.Request.Body, false);
        }

        var inputStream = Encoding.CreateTranscodingStream(httpContext.Request.Body, encoding, Encoding.UTF8, leaveOpen: true);
        return (inputStream, true);
    }
}