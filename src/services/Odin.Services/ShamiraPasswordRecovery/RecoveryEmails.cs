//
// SEB:NOTE
// These are temporary place holders and should probably be
// exchanged with something a bit more professional looking...
//

namespace Odin.Services.ShamiraPasswordRecovery;

public static class RecoveryEmails
{
    //
    // Provisioning Completed
    //
    public static string VerifyEmailText(string email, string domain, string link)
    {
        return @$"
            Hi {email},

            You or someone else has requested to put your identity ({domain}) into recovery mode.

            If you did not do this, delete this email.

            To continue putting your identity into recovery mode, click here: {link}

            --
            Team Homebase
        ";
    }

    public static string VerifyEmailHtml(string domain, string link)
    {
        return @$"
            <!DOCTYPE html>
            <html>
                <head>
                    <meta name='viewport' content='width=device-width' />
                    <meta http-equiv='Content-Type' content='text/html; charset=UTF-8' />
                    <title>Homebase</title>
                    <!--[if mso]><style type='text/css'>h1,h2,h3,h4,p,ul,ol,table td,body {{font-family: sans-serif, Arial, Helvetica !important;}}</style>[endif]-->
                    <style>
                        @media only screen and (max-width: 620px) {{
                            table[class='body'] h1 {{
                                font-size: 28px !important;
                                margin-bottom: 10px !important;
                            }}
                            table[class='body'] a,
                            table[class='body'] ol,
                            table[class='body'] p,
                            table[class='body'] span,
                            table[class='body'] td,
                            table[class='body'] ul {{
                                font-size: 16px !important;
                            }}
                            table[class='body'] .article,
                            table[class='body'] .wrapper {{
                                padding: 10px !important;
                            }}
                            table[class='body'] .content {{
                                padding: 0 !important;
                            }}
                            table[class='body'] .container {{
                                padding: 0 !important;
                                width: 100% !important;
                            }}
                            table[class='body'] .main {{
                                border-left-width: 0 !important;
                                border-radius: 0 !important;
                                border-right-width: 0 !important;
                            }}
                            table[class='body'] .btn table {{
                                width: 100% !important;
                            }}
                            table[class='body'] .btn a {{
                                width: 100% !important;
                            }}
                            table[class='body'] .img-responsive {{
                                height: auto !important;
                                max-width: 100% !important;
                                width: auto !important;
                            }}
                        }}
                    </style>
                </head>
                <body
                    style='
                        background-color: #f6f6f6;
                        font-family: sans-serif, Arial, Helvetica !important;
                        -webkit-font-smoothing: antialiased;
                        font-size: 14px;
                        line-height: 1.4;
                        margin: 0;
                        padding: 0;
                        -ms-text-size-adjust: 100%;
                        -webkit-text-size-adjust: 100%;
                    '
                >
                    <table
                        border='0'
                        cellpadding='0'
                        cellspacing='0'
                        class='body'
                        style='
                            border-collapse: separate;
                            mso-table-lspace: 0;
                            mso-table-rspace: 0;
                            background-color: #f6f6f6;
                            width: 100%;
                        '
                        width='100%'
                        bgcolor='#f6f6f6'
                    >
                        <tr>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                            <td
                                class='container'
                                style='
                                    font-size: 14px;
                                    vertical-align: top;
                                    display: block;
                                    max-width: 580px;
                                    padding: 10px;
                                    width: 580px;
                                    margin: 0 auto;
                                '
                                width='580'
                                valign='top'
                            >
                                <div
                                    class='content'
                                    style='
                                        box-sizing: border-box;
                                        display: block;
                                        margin: 0 auto;
                                        max-width: 580px;
                                        padding: 10px;
                                    '
                                >
                                    <table
                                        class='main'
                                        style='
                                            border-collapse: separate;
                                            mso-table-lspace: 0;
                                            mso-table-rspace: 0;
                                            background: #fff;
                                            border-radius: 3px;
                                            width: 100%;
                                        '
                                        width='100%'
                                    >
                                        <tr>
                                            <td
                                                class='wrapper'
                                                style='
                                                    font-size: 14px;
                                                    vertical-align: top;
                                                    box-sizing: border-box;
                                                    padding: 20px;
                                                '
                                                valign='top'
                                            >
                                                <table
                                                    border='0'
                                                    cellpadding='0'
                                                    cellspacing='0'
                                                    style='
                                                        border-collapse: separate;
                                                        mso-table-lspace: 0;
                                                        mso-table-rspace: 0;
                                                        width: 100%;
                                                    '
                                                    width='100%'
                                                >
                                                    <tr>
                                                        <td
                                                            style='
                                                                font-size: 14px;
                                                                vertical-align: top;
                                                                font-weight: 400;
                                                                margin: 0;
                                                            '
                                                            valign='top'
                                                        >
                                                            <p style='margin-bottom: 15px'>Hi there,</p>
                                                            <p style='margin-bottom: 15px'>
                                                                You or someone else has requested to put your identity ({domain}) into recovery mode.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                If you did not do this, delete this email.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                To continue putting your identity into recovery mode, click <a href='{link}' style='text-decoration:underline;'>here</a>.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                Kind regards<br />Team Homebase
                                                            </p>
                                                        </td>
                                                    </tr>
                                                </table>
                                            </td>
                                        </tr>
                                    </table>
                                    <div
                                        class='footer'
                                        style='clear: both; margin-top: 10px; text-align: center; width: 100%'
                                    >
                                        <table
                                            border='0'
                                            cellpadding='0'
                                            cellspacing='0'
                                            style='
                                                border-collapse: separate;
                                                mso-table-lspace: 0;
                                                mso-table-rspace: 0;
                                                width: 100%;
                                            '
                                            width='100%'
                                        >
                                            <tr>
                                                <td
                                                    class='content-block powered-by'
                                                    style='
                                                        vertical-align: top;
                                                        padding-bottom: 10px;
                                                        padding-top: 10px;
                                                        color: #999;
                                                        font-size: 12px;
                                                        text-align: center;
                                                    '
                                                    valign='top'
                                                    align='center'
                                                >
                                                    <a
                                                        href='https://homebase.id'
                                                        style='
                                                            color: #999;
                                                            font-size: 12px;
                                                            text-align: center;
                                                            text-decoration: none;
                                                        '
                                                        >Homebase</a
                                                    >
                                                </td>
                                            </tr>
                                        </table>
                                    </div>
                                </div>
                            </td>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                        </tr>
                    </table>
                </body>
            </html>"
            ;
    }
}
