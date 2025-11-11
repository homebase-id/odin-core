using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services;

namespace Odin.Hosting.Controllers.Anonymous;

// SEB:NOTE this is temporary code to test KMP capabilities. It doesn't do anything but redirect back to the app.

public class YouAuthKmpController : ControllerBase
{
    // curl https://frodo.dotyou.cloud/api/v1/kmp/auth
    [HttpGet("/api/v1/kmp/auth")]
    public ActionResult Auth()
    {
        const string html = """

                                <!DOCTYPE html>
                                <html lang='en'>
                                <head>
                                    <meta charset='utf-8' />
                                    <meta name='viewport' content='width=device-width, initial-scale=1, user-scalable=no' />
                                    <style>
                                        body {
                                            margin: 0;
                                            padding: 20px;
                                            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                                            font-size: 18px;
                                            line-height: 1.5;
                                            text-align: center;
                                            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                                            min-height: 100vh;
                                            display: flex;
                                            flex-direction: column;
                                            justify-content: center;
                                            align-items: center;
                                        }
                                        
                                        .container {
                                            background: white;
                                            padding: 30px;
                                            border-radius: 12px;
                                            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
                                            width: 100%;
                                            max-width: 400px;
                                        }
                                        
                                        .title {
                                            color: #333;
                                            margin-bottom: 20px;
                                            font-size: 24px;
                                            font-weight: 600;
                                        }
                                        
                                        .message {
                                            color: #666;
                                            margin-bottom: 30px;
                                            font-size: 18px;
                                        }
                                        
                                        .redirect-btn {
                                            background: #007AFF;
                                            color: white;
                                            border: none;
                                            border-radius: 12px;
                                            padding: 16px 32px;
                                            font-size: 18px;
                                            font-weight: 600;
                                            cursor: pointer;
                                            min-height: 48px; /* Touch target size */
                                            min-width: 120px;
                                            transition: all 0.2s ease;
                                            width: 100%;
                                        }
                                        
                                        .redirect-btn:active {
                                            background: #0051D5;
                                            transform: scale(0.98);
                                        }
                                        
                                        .spinner {
                                            margin: 20px auto;
                                            width: 32px;
                                            height: 32px;
                                            border: 3px solid #f3f3f3;
                                            border-top: 3px solid #007AFF;
                                            border-radius: 50%;
                                            animation: spin 1s linear infinite;
                                        }
                                        
                                        @keyframes spin {
                                            0% { transform: rotate(0deg); }
                                            100% { transform: rotate(360deg); }
                                        }
                                    </style>
                                </head>
                                <body>
                                    <div class='container'>
                                        <h1 class='title'>Authenticating</h1>
                                        <!--<div class='spinner'></div>-->
                                        <!--<p class='message'>Please wait while we process your authentication...</p>-->
                                        <button id='redirectBtn' class='redirect-btn'>Continue to App</button>
                                    </div>
                                    
                                    <script>
                                        document.getElementById('redirectBtn').addEventListener('click', function() {
                                            window.location.href = 'youauth://callback?code=abc123';
                                        });
                                    </script>
                                </body>
                                </html>
                            """;

        return Content(html, "text/html");
    }
}
