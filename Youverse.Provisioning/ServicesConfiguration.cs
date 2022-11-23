using Dawn;
using Youverse.Provisioning.Services.Certificate;
using Youverse.Provisioning.Services.Registration;

namespace Youverse.Provisioning;

public class ServicesConfiguration
{
    public void ConfigureServices(IServiceCollection services, ProvisioningConfig config)
    {
        services.AddControllers();
        AssertValidConfiguration(config);
        PrepareEnvironment(config);

        services.AddSingleton<ProvisioningConfig>(svc => config);
        services.AddSingleton<IRegistrationService, RegistrationService>();
        services.AddSingleton<ICertificateService, LetsEncryptCertificateService>();

        services.AddSingleton<CertificateOrderList>();
        services.AddHostedService<LetsEncryptCertificateOrderStatusChecker>();

        var origins = new string[] { "https://*.youver.se", "http://localhost:5002" };
        services.AddCors(options =>
        {
            options.AddPolicy("allowedOrigins",
                builder =>
                {
                    builder
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .WithOrigins(string.Join(',', config.AllowedOrigins ?? new List<string>()))
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
        });

        Console.WriteLine($"Added Origins: \n{string.Join("\n", origins)}");
    }

    private void AssertValidConfiguration(ProvisioningConfig config)
    {
        Guard.Argument(config, nameof(config)).NotNull();
        Guard.Argument(config.CertificateRootPath, nameof(config.CertificateRootPath)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateChallengeTokenPath, nameof(config.CertificateChallengeTokenPath)).NotNull().NotEmpty();
        Guard.Argument(config.DataRootPath, nameof(config.DataRootPath)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateAuthorityAssociatedEmail, nameof(config.CertificateAuthorityAssociatedEmail)).NotNull().NotEmpty();
        Guard.Argument(config.NumberOfCertificateValidationTries, nameof(config.NumberOfCertificateValidationTries)).Min(3);
        Guard.Argument(config.CertificateSigningRequest, nameof(config.CertificateSigningRequest)).NotNull();

        Guard.Argument(config.CertificateSigningRequest.Locality, nameof(config.CertificateSigningRequest.Locality)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateSigningRequest.Organization, nameof(config.CertificateSigningRequest.Organization)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateSigningRequest.State, nameof(config.CertificateSigningRequest.State)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateSigningRequest.CountryName, nameof(config.CertificateSigningRequest.CountryName)).NotNull().NotEmpty();
        Guard.Argument(config.CertificateSigningRequest.OrganizationUnit, nameof(config.CertificateSigningRequest.OrganizationUnit)).NotNull().NotEmpty();
    }

    private void PrepareEnvironment(ProvisioningConfig config)
    {
        Directory.CreateDirectory(config.CertificateChallengeTokenPath);
        Directory.CreateDirectory(config.CertificateRootPath);
        Directory.CreateDirectory(config.DataRootPath);
    }
}