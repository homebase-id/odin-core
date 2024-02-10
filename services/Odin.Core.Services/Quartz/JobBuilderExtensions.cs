using System;
using Quartz;

namespace Odin.Core.Services.Quartz;

 public static class JobBuilderExtensions
 {

     //

     public static string GetGroupName<TJobType>(this JobBuilder _)
     {
         return Helpers.GetGroupName<TJobType>();
     }

     //

     public static JobKey ParseJobKey(this JobBuilder _, string jobKey)
     {
         return Helpers.ParseJobKey(jobKey);
     }

     //

     public static JobKey CreateUniqueJobKey(this JobBuilder _)
     {
         return Helpers.CreateUniqueJobKey();
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
         builder.UsingJobData(JobConstants.RetryCountKey, 0.ToString());
         builder.UsingJobData(JobConstants.RetryMaxKey, retryMax.ToString());
         builder.UsingJobData(JobConstants.RetryDelaySecondsKey, retrySeconds.ToString());
         return builder;
     }

     public static JobBuilder WithRetry(
         this JobBuilder builder,
         int retryMax)
     {
         return builder.WithRetry(retryMax, TimeSpan.FromSeconds(0));
     }

     //

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
         builder.UsingJobData(JobConstants.CompletedRetentionSecondsKey, completedRetentionSeconds.ToString());
         builder.UsingJobData(JobConstants.FailedRetentionSecondsKey, failedRetentionSeconds.ToString());
         return builder;
     }

     //

     public static JobBuilder WithJobEvent<T>(this JobBuilder builder) where T : IJobEvent
     {
         builder.UsingJobData(JobConstants.JobEventTypeKey, typeof(T).AssemblyQualifiedName);
         return builder;
     }

     //

 }
