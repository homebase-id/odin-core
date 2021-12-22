using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Profile;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive.Storage
{
    public class ProfileDataIndexer : IDriveMetadataIndexer
    {
        private readonly ILogger<object> _logger;

        private readonly IGranteeResolver _granteeResolver;

        private readonly IProfileAttributeManagementService _profileService;

        public ProfileDataIndexer(IGranteeResolver granteeResolver, ILogger<object> logger, IProfileAttributeManagementService profileService)
        {
            _granteeResolver = granteeResolver;
            _logger = logger;
            _profileService = profileService;
        }

        public async Task Rebuild(StorageDriveIndex index)
        {
            if (Directory.Exists(index.IndexRootPath))
            {
                Directory.Delete(index.IndexRootPath, true);
            }

            Directory.CreateDirectory(index.IndexRootPath);

            await RebuildDataAttributes(index);
        }

        private async Task RebuildDataAttributes(StorageDriveIndex index)
        {
            //_profileAttributeReader.GetAttributes()

            using var indexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, index.GetQueryIndexPath(), index.QueryIndexName);
            await indexStorage.EnsureIndex(x => x.FileId, true);
            
            var indexedItem = new IndexedItem()
            {
                CategoryId = Guid.NewGuid(),
                CreatedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                LastUpdatedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                FileId = Guid.NewGuid(),
                JsonContent = JsonConvert.SerializeObject(new { message = "Suzy's boots", previewImage = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCABkAGQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDorGdNNWRWbd5n95fu1G3iJZGVZE2qn93+Kufh8bW+oWypcM0E33fm+Zf++qq3lzbxx72vIG/3Wr5Z4SpGWsT7SNanKPMdxb67b+XItr+5kb5vm/irLXxJPFOzTJDMq/3lrjW1awjZm+1K3+6rVl69qSTvH9ndtv8Ast8tbUsBOpO0jKdelTg5Hp1x4oSWLbHB5f8AtK1Q2+pJdv8AvpVj3fxV5nZ31rbNbzskkzbfmXzPl3f7tO1bX3u5P9FRoY1/2q3WWz5uVGaxVJR5j0a+1Se22rC8Ei/wttrk9Y1nVPmlkZVj/urXJ/bp9+5pZG/7aNV6TXbxo1RfLjX/AGVrpjls6cu5jLMIS8jat/EnmWzJN5m3b838VZt9Lbzsr26TLIq7dzfKrf71Za3b+Yzt95qGlZm+au2ngY05c0TgrY6dSPKXmk+WmrJVHzKkWZV/irq9mcJfDf7tFVxMv96isfZgZTLRtr0DwL4XS+juLzWPNis9q+UsTLudv9rd92qvijwvBbPJPot4zRxfN5U6/Mu75flZfvVyfXoxnyHrewly3OH2tR93+Ja9a0Pwdo0GlLda1tu2ljWaPazRrtZf9mrtvrWnaVEqaPoNlGqt97ylZ/8Avpvmqf7Uj8MYjWElL4TyCO2nWRXa3naPd837tq3I9UsoU+9JH8v+r27a7jU/FK6i6/azd7tu1lePcq1Ytdd0toP3lks7R/djaJW/9CrKeP5t4HXQpToR9xnj9xOk9zI8aqqs33Vpq/N935q9gm8QWV6nlTaRbSbfurPFH8v/AI7VXVrbw/JBumsLKOaT7zRx/wDoP3a1jmi+GUTllgZSlzHlO6nfw/davSoYPDUVt5S6J5/96Rp23NVGbSdDubaSK1t7uBv4W8/zFX/vpa0jmkP5TP8As6Rwe7+7RuauuXwkkm5YZZmb+H7v/oNb3h/wlZeQ32yBWZfvNL8taSzSjH4Q/s2r1PN16fxfgtFemXWkaEJ28qygdfXcw/8AZqK5/wC1YeZr/Zc/Iha9tWto0kt2WRf4lb73+9WT4g1BGtVW1RWjkbbJtb95t/ur/vfd3VMu1n3eVHuX+8tR65qDW2mSLJ8zS/uY4938Tf53f8Bry4/EejU+GXvFzSdSgi0i3t7h55o0+bb5n7tdzbvl/wC+qsLqVlHIzRwQLt+ZWkl3Vj6PJLJpCuzzfvP9XuVfmj/h+X/vpv8AgVSNGq3MbtBA25WX5o12q397/wAdocY8z5gpylGKLTapBPOu51kX+7HtoaSXd8vkeX/tNUKyef8AxKrL/Csart/8dqOO7T7SyLOzSL95Wj+VankX2UX7b+YuNA0ka7nsmb/aZvlps0bt/HaLt/5aK27b/wABqnNfQKv7xo2/65rtrNmvkk/ijjjVvlVW+9W1OhKRlUxEImlcfYt377UbuT/ZVl/9lWi1udNgX/VX8kf95p9rf+O1gxzIsrfJ5ip97av3ao3V289s09i25dv3lVm211exjy8tzhljJbm42pSxXTfZ3kWNm+VZJGk21ak1bbFtk1K4b5fuxLXP2scsqK6o0it/FWtDZRRxbpoFaRv4pJPu1TjS+0Vhq05cxWe4DtuE9z+MmKKuIkKKABbj6qxorX2tP+U3sZkN9fw/duFb/eWqd4t1exKk1w21W3L/AOg/+zVa2/7P/jtO8t2k2Ku7/d+aj2cY+8eGpVpe7cLe7vYoIYFum2oqqvyr/DU0dzeyzxqsrSMrblVl/iq9a6PuXdM3/AY1q95tlYbfLiXzP9lfmrzKuOouXs6EOaR7VDLK6j7SvPlj6mfNHcWX724uo/tDf8s413VTWVpPMWS4khZ/4lXdTryf7Tdq8n7tfu/NVXd8u3bXdQpS5Pf+I4q9aPP7nwjm8Oyysz/aJLvd/dn+X/vn5ar/ANhJaN/x5NH833trVYt5GjkV1do/7vzba2NP1afbtmTzI1X5pF+8v+1UVZYin8NpfgyqVLC1vdleMvvMHylXdtTbu+8ysy0Qs0UapDPcwxr91Y5flrqLi2s7v5o1+b+9Gvzf981lzaRPH80a+Yv+7trGljqM/dqe7LzLrZbiKXvUpc0fIy45LiOJYo7+5WNflVW2ttqGaTUW/wCX35f9pdtWGXbu3L/vUbW2/N/3y1d3JFnn+1qoptJqWF2X5247vmirXzUU+Uz9vU7nRQ6bBFBtaWGaTd/FViOG3iVWZFX+7sWoF8uBd7zWy7l+7u3VB9tt9331b/ZRmXdXyEqtervJn6FCjhsPG0Io0pru3ggUxyqu77y1zN1LFPcs+3y12/xNt3f99Va1KVLlfPVmVvu7V+7Wbt/65qzfxSbtte7lmEjRh7RfEz5vN8bOpP2CtyhHub5V/wCBNVhkdZ1ikVVk3eX838Tf71OXyl2vcQM0Mv8Aq/3jL/8AtVV3M0rJvZlX+Fq9Q8Xl9nH3iS4Vo7llby1Zf4Y/mVacqvI22NZGZvvLVdmdZW3bfL/vf3aaqt5reSrSNIu5dzfL/u0c3L7pnGp7xajlltJ1ePdHJ91t1a1nq0/mszQRySS7tvzbfu1j3C3ECyfaFVW2qzR/eao1nWS2j2o3zfdZf4f+A1z1cNSr/HG52UMXVw0rRlbyOmmhbUI1lmsJI2b7skUi1i3lpcWkkizI3y/eZqdZ+ajQyq7M33flb7v+z92tJdWnmlWCFWljb5ZPNX5a4f3uClyw96H5fmeq/YY2PNP3ZHOSqnmHDfmtFdU+l25bdLHYI7cletFP+2KP8pj/AGHV/mMqO9gb5Wby1/u7ty/+g1Xmgs7mTfuhaZf7yKtFrJE0jfaJVhVV+8sa7mrHuo7qdv3eot5K/wC78v8A3zUUMFy+/F6mmJzbmjyzSf8AXqy9cXNgrSRQrPub+KTaqr/s1cjvk02SOXT4pIGVf9ZJIsir/wCO1k2NlZ/N5zyTSfxSLVxmi8zbGkir/D827bXqRjf4jwlKT95NE0M8HlTPcI07Nt8tvM+7UlxFZxy7rW6WRWXd5bblkqvDB80m5/JZfm2yfLu/2VqHc7NuVfu/MzL/AA1pyR+IpSlGK5lzEdxsVN8j+X8v3m/hp0Md1K261f7TH/F8v3f++abN58cTKzwt/daRdu2jT9QbT5FlaVZpN38PzK3+zWc5fymMeXmtMtQs6szxqytH/s/w1at9u5pb7cvyt5SrErKzVnzb2n81Xb5t26Nm+WpLeSyitmg+z38l9J/y08xWijqpSNqU7BMyqsbR/wCsWP5lb+JqIYfMZfL8xpG+8tE0jTs27zJGb5dv8VXF1C4tE8pW8tV/haNflo+z7oRlDm5nsWbSTTRAvnR3Mb9183NFZ8t/cXZWSSdpGxjd81Fc7wsf53952rNUvsL7jnbaTzrtUZEH+0Bg1HrtukU8fl5XK84PWiig86H8VG1odun9lmTncZF71LJAIHbY8h5zhmzRRXRH4QqfEhi3k93dItxK0hKKNxPPFVtUTykWWJmSQ9Sp60UUvsCm/eLrIrNEG+YOvINE0UaxfKir9KKK0nvEip8cfkSWUCm4iiy2wt0zWtcaTbxozBpT/sluKKKirvE66SXJIw1O/eWAO05FJBITI6YG0e1FFaw+M5o/Ebej2EM9oXkL7t5HDUUUU5bs64xVlof/2Q==" })
            };

            await indexStorage.Save(indexedItem);

            using var indexPermissionStorage = new LiteDBSingleCollectionStorage<IndexedItemPermissionGrant>(_logger, index.GetPermissionIndexPath(), index.PermissionIndexName);

            var permission = new IndexedItemPermissionGrant()
            {
                FileId = indexedItem.FileId,
                Permission = Permission.Read,
                DomainIdentity = "frodobaggins.me",
                GranteeId = Guid.NewGuid()
            };

            await indexPermissionStorage.Save(permission);
            indexPermissionStorage.Dispose();
        }
    }
}