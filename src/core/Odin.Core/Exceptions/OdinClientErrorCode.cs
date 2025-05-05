namespace Odin.Core.Exceptions;

public enum OdinClientErrorCode
{
    NoErrorCode = 0,
    UnhandledScenario = 1,
    ArgumentError = 2,

    // Auth Errors 10xx
    InvalidAuthToken = 1001,
    SharedSecretEncryptionIsInvalid = 1002,
    PublicKeyEncryptionIsInvalid = 1004,
    
    // Notifcation Errors 20xx
    InvalidNotificationType = 2001,
    UnknownNotificationId = 2002,


    // Circle Errors 30xx
    AtLeastOneDriveOrPermissionRequiredForCircle = 3001,
    CannotAllowCirclesOnAuthenticatedOnly = 3002,
    CannotAllowCirclesOrIdentitiesOnAnonymousOrOwnerOnly = 3003,
    CannotDeleteCircleWithMembers = 3004,
    IdentityAlreadyMemberOfCircle = 3005,
    NotAConnectedIdentity = 3006,
    NotAFollowerIdentity = 3007,
    IdentityNotFollowed = 3008,
    IdentityAlreadyFollowed = 3009,
    CannotGrantAutoConnectedMoreCircles = 3010,
    IncomingRequestNotFound = 3011,

    // Drive mgmt errors 40xx
    CannotAllowAnonymousReadsOnOwnerOnlyDrive = 4001,
    CannotUpdateNonActiveFile = 4002,
    DriveAliasAndTypeAlreadyExists = 4003,
    InvalidGrantNonExistingDrive = 4004,
    CannotAllowSubscriptionsOnOwnerOnlyDrive = 4004,

    // Drive errors 41xx
    CannotOverwriteNonExistentFile = 4101,
    CannotUploadEncryptedFileForAnonymous = 4102,
    DriveSecurityAndAclMismatch = 4104,
    ExistingFileWithUniqueId = 4105,
    FileNotFound = 4106,
    IdAlreadyExists = 4107,
    InvalidInstructionSet = 4108,
    InvalidKeyHeader = 4109,
    InvalidRecipient = 4110,
    InvalidTargetDrive = 4111,
    InvalidThumnbnailName = 4112,
    InvalidTransferFileType = 4113,
    InvalidTransferType = 4114,
    MalformedMetadata = 4115,
    MissingUploadData = 4116,
    TransferTypeNotSpecified = 4117,
    UnknownId = 4118,
    InvalidPayload = 4119,
    CannotUseReservedFileType = 4120,
    InvalidReferenceFile = 4122,
    CannotUseReferencedFileOnStandardFiles = 4123,
    CannotUseGroupIdInTextReactions = 4124,
    InvalidFileSystemType = 4125,
    InvalidDrive = 4126,
    InvalidChunkStart = 4128,
    MissingVersionTag = 4159,
    VersionTagMismatch = 4160,
    InvalidFile = 4161,
    InvalidQuery = 4162,
    InvalidUpload = 4163,
    InvalidPayloadNameOrKey = 4164,
    FileLockedDuringWriteOperation = 4165,
    InvalidGlobalTransitId = 4166,


    // Connection errors 50xx
    NotAnAutoConnection = 5001,
    IdentityMustBeConnected = 5002,
    ConnectionRequestToYourself = 5003,
    BlockedConnection = 5004,
    CannotSendConnectionRequestToValidConnection = 5005,
    RemoteServerMissingOutgoingRequest = 5006,

    // App or YouAuth Domain Errors 60xx
    AppNotRegistered = 6001,
    AppRevoked = 6002,
    DomainNotRegistered = 6050,
    AppHasNoAuthorizedCircles = 6700,
    InvalidAccessRegistrationId = 6800,
    InvalidCorsHostName = 6850,

    //Transit errors
    RemoteServerReturnedForbidden = 7403,
    RemoteServerReturnedInternalServerError = 7500,
    RemoteServerReturnedUnavailable = 7503,
    RemoteServerTransitRejected = 7900,
    InvalidTransitOptions = 7901,
    FileDoesNotHaveSender = 7902,
    MissingGlobalTransitId = 7903,
    RemoteServerIsNotAnOdinServer = 7904,
    RemoteServerOfflineOrUnavailable = 7905,
    

    RegistrationStatusNotReadyForFinalization = 8001,

    // System Errors 90xx
    InvalidFlagName = 9001,
    NotInitialized = 9002,
    UnknownFlagName = 9003,
    InvalidOrExpiredRsaKey = 9004,
    MissingVerificationHash = 9005,
    PasswordAlreadySet = 9006,
    IntroductoryRequestAlreadySent = 9007,
}