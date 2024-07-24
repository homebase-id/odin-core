using System;
using Quartz;

namespace Odin.Services.JobManagement;

 public static class OldJobBuilderExtensions
 {

     //

     public static string GetGroupName<TJobType>(this JobBuilder _)
     {
         return OldHelpers.GetGroupName<TJobType>();
     }

     //

     public static JobKey ParseJobKey(this JobBuilder _, string jobKey)
     {
         return OldHelpers.ParseJobKey(jobKey);
     }

     //

     public static JobKey CreateUniqueJobKey(this JobBuilder _)
     {
         return OldHelpers.CreateUniqueJobKey();
     }

     //

     public static JobBuilder WithRetry(
         this JobBuilder builder,
         int retryMax,
         TimeSpan retryDelay)
     {
         if (retryMax < 0)
         {
             throw new ArgumentOutOfRangeException(nameof(retryMax), "Retry max must be greater than or equal to 0");
         }

         var retrySeconds = (long)retryDelay.TotalSeconds;
         if (retrySeconds < 0)
         {
             throw new ArgumentOutOfRangeException(nameof(retryDelay), "Retry delay must be greater than or equal to 0 zero seconds");
         }

         // NOTE: values must be strings if "storeOptions.UseProperties = true;"
         builder.UsingJobData(OldJobConstants.RetryCountKey, 0.ToString());
         builder.UsingJobData(OldJobConstants.RetryMaxKey, retryMax.ToString());
         builder.UsingJobData(OldJobConstants.RetryDelaySecondsKey, retrySeconds.ToString());
         return builder;
     }

     public static JobBuilder WithRetry(
         this JobBuilder builder,
         int retryMax)
     {
         return builder.WithRetry(retryMax, TimeSpan.FromSeconds(0));
     }

     //

     // How long to keep job data after job completion.
     public static JobBuilder WithRetention(
         this JobBuilder builder,
         TimeSpan retention)
     {
         return builder.WithRetention(retention, retention);
     }

     public static JobBuilder WithRetention(
         this JobBuilder builder,
         TimeSpan completedRetention,
         TimeSpan failedRetention)
     {
         var completedRetentionSeconds = (long)completedRetention.TotalSeconds;
         if (completedRetentionSeconds < 0)
         {
             throw new ArgumentOutOfRangeException(nameof(completedRetentionSeconds), "Retention seconds must be greater than or equal to 0");
         }

         var failedRetentionSeconds = (long)failedRetention.TotalSeconds;
         if (failedRetentionSeconds < 0)
         {
             throw new ArgumentOutOfRangeException(nameof(failedRetentionSeconds), "Retention seconds must be greater than or equal to 0");
         }

         // NOTE: values must be strings if "storeOptions.UseProperties = true;"
         builder.StoreDurably();
         builder.UsingJobData(OldJobConstants.CompletedRetentionSecondsKey, completedRetentionSeconds.ToString());
         builder.UsingJobData(OldJobConstants.FailedRetentionSecondsKey, failedRetentionSeconds.ToString());
         return builder;
     }

     //

     public static JobBuilder WithJobEvent<T>(this JobBuilder builder) where T : OldIJobEvent
     {
         builder.UsingJobData(OldJobConstants.JobEventTypeKey, typeof(T).AssemblyQualifiedName);
         return builder;
     }

     //

 }
