//
// SEB:NOTE
// These are temporary place holders and should probably be 
// exchanged with something a bit more professional looking...
//
namespace Odin.Core.Services.Registry.Registration;

public static class RegistrationEmails
{
  //
  // Provisioning Completed
  //
  public static string ProvisioningCompletedText(string email, string domain, string link)
  {
    return @$" 
            Hi {email},
            
            Your new {domain} identity is ready.
            
            Please click here {link} to go to it!
            
            --
            Team Odin
        ";
  }
    
  public static string ProvisioningCompletedHtml(string email, string domain, string link)
  {
    return @$" 
            <!DOCTYPE html>
            <html>
            <head>
              <title>HTML Email Template</title>
              <style>
                body {{
                  font-family: Arial, sans-serif;
                  line-height: 1.5;
                  color: #333333;
                }}

                .container {{
                  max-width: 600px;
                  margin: 0 auto;
                  padding: 20px;
                }}

                h1 {{
                  color: #0099cc;
                }}

                p {{
                  margin-bottom: 20px;
                }}
              </style>
            </head>
            <body>
              <div class='container'>
                <h1>Hi there!</h1>
                <p>Your new identity is ready.</p>
                <p>
                  Click <a href='{link}'>here</a> to go to it.
                </p>
                <p>
                  --<br />
                  Team Odin
                </p>
              </div>
            </body>
            </html>
        ";
  }
}