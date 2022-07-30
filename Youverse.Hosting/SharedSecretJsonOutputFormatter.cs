#nullable enable
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting;

/// <summary>
/// A <see cref="TextOutputFormatter"/> for JSON content that uses <see cref="JsonSerializer"/>.
/// </summary>
public class SharedSecretJsonOutputFormatter : TextOutputFormatter
{
    /// <summary>
    /// Initializes a new <see cref="SharedSecretJsonOutputFormatter"/> instance.
    /// </summary>
    /// <param name="jsonSerializerOptions">The <see cref="JsonSerializerOptions"/>.</param>
    public SharedSecretJsonOutputFormatter(JsonSerializerOptions jsonSerializerOptions)
    {
        SerializerOptions = jsonSerializerOptions;

        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    internal static SystemTextJsonOutputFormatter CreateFormatter(JsonOptions jsonOptions)
    {
        var jsonSerializerOptions = jsonOptions.JsonSerializerOptions;

        if (jsonSerializerOptions.Encoder is null)
        {
            // If the user hasn't explicitly configured the encoder, use the less strict encoder that does not encode all non-ASCII characters.
            jsonSerializerOptions = new JsonSerializerOptions(jsonSerializerOptions)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
        }

        return new SystemTextJsonOutputFormatter(jsonSerializerOptions);
    }

    /// <summary>
    /// Gets the <see cref="JsonSerializerOptions"/> used to configure the <see cref="JsonSerializer"/>.
    /// </summary>
    /// <remarks>
    /// A single instance of <see cref="SystemTextJsonOutputFormatter"/> is used for all JSON formatting. Any
    /// changes to the options will affect all output formatting.
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; }

    protected override bool CanWriteType(Type? type)
    {
        return base.CanWriteType(type);
    }

    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        return base.CanWriteResult(context);
    }

    /// <inheritdoc />
    public sealed override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (selectedEncoding == null)
        {
            throw new ArgumentNullException(nameof(selectedEncoding));
        }

        var httpContext = context.HttpContext;

        // context.ObjectType reflects the declared model type when specified.
        // For polymorphic scenarios where the user declares a return type, but returns a derived type,
        // we want to serialize all the properties on the derived type. This keeps parity with
        // the behavior you get when the user does not declare the return type and with Json.Net at least at the top level.
        var objectType = context.Object?.GetType() ?? context.ObjectType ?? typeof(object);

        var responseStream = httpContext.Response.Body;
        if (selectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
                var accessor = context.HttpContext.RequestServices.GetRequiredService<DotYouContextAccessor>();
                var key = accessor.GetCurrent()?.PermissionsContext?.SharedSecretKey ?? Guid.Empty.ToByteArray().ToSensitiveByteArray(); //hack
                
                var targetStream = new MemoryStream();
                await JsonSerializer.SerializeAsync(targetStream, context.Object, objectType, SerializerOptions, httpContext.RequestAborted);
                
                var iv = ByteArrayUtil.GetRndByteArray(16);
                var encryptedBytes = AesCbc.Encrypt(targetStream.GetBuffer(), ref key, iv);

                //wrap in our object
                SharedSecretEncryptedPayload encryptedPayload = new SharedSecretEncryptedPayload()
                {
                    Iv = iv,
                    Data = Convert.ToBase64String(encryptedBytes)
                };

                await JsonSerializer.SerializeAsync(responseStream, encryptedPayload, encryptedPayload.GetType(), SerializerOptions, httpContext.RequestAborted);
                await responseStream.FlushAsync(httpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        }
        else
        {
            // JsonSerializer only emits UTF8 encoded output, but we need to write the response in the encoding specified by
            // selectedEncoding
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, selectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                //TODO: implement here too
                await JsonSerializer.SerializeAsync(transcodingStream, context.Object, objectType, SerializerOptions);
                await transcodingStream.FlushAsync();
            }
            catch (Exception ex)
            {
                // TranscodingStream may write to the inner stream as part of it's disposal.
                // We do not want this exception "ex" to be eclipsed by any exception encountered during the write. We will stash it and
                // explicitly rethrow it during the finally block.
                exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    await transcodingStream.DisposeAsync();
                }
                catch when (exceptionDispatchInfo != null)
                {
                }

                exceptionDispatchInfo?.Throw();
            }
        }
    }
}