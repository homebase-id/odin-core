using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Serialization;

/// <summary>
/// Provisions a REAL app registration and a REAL connection (into a circle that grants a drive),
/// then reads the raw serialized JSON blobs straight out of the tenant SQLite database and prints them.
/// These blobs are "snapshots" of the real on-the-wire serialization format.
/// </summary>
public class RealBlobCaptureTest
{
    // AppRegistrationService.AppRegistrationDataType
    private static readonly Guid AppRegistrationDataType = Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d");

    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    [Explicit]
    public async Task CaptureRealBlobs()
    {
        var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        const DrivePermission drivePermissions = DrivePermission.ReadWrite;

        //
        // 1) Recipient creates a target drive (Sam)
        //
        var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        //
        // 2) Sender needs the same drive in order to send across files (Frodo)
        //
        var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on sender",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

        //
        // 3) Recipient creates a circle that grants a drive permission (Sam)
        //
        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = targetDrive,
            Permission = drivePermissions
        };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
            });
        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        //
        // 4) Register a REAL app on the recipient (Sam) with a drive grant AND an authorized circle.
        //    This produces a fully-populated AppRegistration graph
        //    (Grant.KeyStoreKeyEncryptedDriveGrants + CircleMemberPermissionSetGrantRequest).
        //
        var appId = Guid.NewGuid();
        var appPermissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections, PermissionKeys.ReadCircleMembership)
        };

        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        };

        await recipientOwnerClient.AppManager.RegisterApp(
            appId,
            appPermissions,
            authorizedCircles: new List<Guid>() { circleId },
            circleMemberGrantRequest: circleMemberGrant);

        // Register a client so the app grant is realized fully
        await recipientOwnerClient.AppManager.RegisterAppClient(appId);

        //
        // 5) Sender sends connection request, recipient accepts INTO the circle that grants the drive
        //
        await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>());
        await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId,
            new List<GuidId>() { circleId });

        //
        // Validate the connection has the expected circle/drive grant before reading blobs
        //
        var getConnectionInfoResponse = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity.OdinId);
        ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
        var senderConnectionInfo = getConnectionInfoResponse.Content;
        ClassicAssert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));

        //
        // 6) Read the raw serialized JSON blobs straight out of the tenant SQLite database(s) and print them.
        //
        var tenantsRoot = Path.Combine(_scaffold.TestDataPath, "tenants");
        TestContext.WriteLine($"// tenants root: {tenantsRoot}");

        var dbFiles = Directory.GetFiles(tenantsRoot, "identity.db", SearchOption.AllDirectories);
        TestContext.WriteLine($"// found {dbFiles.Length} identity.db file(s)");

        var appRegHex = ToHex(AppRegistrationDataType.ToByteArray());

        foreach (var dbPath in dbFiles)
        {
            TestContext.WriteLine($"// ======== DB: {dbPath} ========");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();

            // -- AppRegistration blobs (KeyThreeValue, key3 == AppRegistrationDataType) --
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT data FROM KeyThreeValue WHERE hex(key3) = @key3";
                cmd.Parameters.AddWithValue("@key3", appRegHex);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var bytes = (byte[])reader["data"];
                    var json = Encoding.UTF8.GetString(bytes);
                    TestContext.WriteLine("// ---- AppRegistration ----");
                    TestContext.WriteLine(json);
                }
            }

            // -- IcrAccessRecord blobs (Connections, one row per connected identity) --
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT identity, data FROM Connections";

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var identityValue = reader["identity"]?.ToString();
                    var bytes = (byte[])reader["data"];
                    var json = Encoding.UTF8.GetString(bytes);
                    TestContext.WriteLine($"// ---- IcrAccessRecord ({identityValue}) ----");
                    TestContext.WriteLine(json);
                }
            }
        }

        //
        // Cleanup
        //
        await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }
}
