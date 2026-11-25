using Hamnava.Core.BackgroundJobs;
using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Media;
using Hamnava.Core.Messaging;
using Hamnava.Core.Payments;
using Hamnava.Core.Reports;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hamnava.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddHamnavaCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<PaymentGatewayOptions>(configuration.GetSection(PaymentGatewayOptions.SectionName));
        services.Configure<SmsProviderOptions>(configuration.GetSection(SmsProviderOptions.SectionName));
        services.Configure<MediaStorageOptions>(configuration.GetSection(MediaStorageOptions.SectionName));

        var timeZoneId = configuration["Hamnava:TimeZoneId"] ?? "Asia/Tehran";
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IJalaliDateService>(sp => new JalaliDateService(sp.GetRequiredService<TimeProvider>(), timeZoneId));

        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IAuthorizationScopeService, AuthorizationScopeService>();
        services.AddScoped<IOtpAuthService, OtpAuthService>();

        services.AddScoped<IQuotaService, QuotaService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<IAttendanceService, AttendanceService>();

        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<IFinancialService, FinancialService>();
        services.AddScoped<IInstrumentRentalService, InstrumentRentalService>();
        services.AddScoped<IConsultationService, ConsultationService>();

        services.AddHttpClient<IZarinpalPaymentGateway, ZarinpalPaymentGateway>();
        services.AddScoped<IPaymentOrchestrationService, PaymentOrchestrationService>();

        services.AddHttpClient<ISmsProvider, ApiPayamakSmsProvider>();
        services.AddScoped<ISmsService, SmsService>();

        services.AddSingleton<IMediaStorageProvider, LocalMediaStorageProvider>();
        services.AddScoped<IMediaProcessingService, MediaProcessingService>();

        services.AddScoped<IPdfCertificateService, QuestPdfCertificateService>();
        services.AddScoped<IStoryImageService, SkiaStoryImageService>();

        services.AddScoped<IHamnavaRecurringJobs, HamnavaRecurringJobs>();

        return services;
    }
}
