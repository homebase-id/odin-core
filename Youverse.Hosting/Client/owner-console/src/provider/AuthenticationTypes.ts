//This file just holds the classes returned from the DI server api
//They are Capital-Camel cased until I update the DI with json attributes
export interface NonceData {
    saltPassword64: string,
    saltKek64: string,
    nonce64: string,
    publicPem: string,
    crc: number
}

export interface AuthenticationReplyNonce {
    nonce64: string
    nonceHashedPassword64: string
    crc: number
    rsaEncrypted: string
}

export interface AuthenticationPayload {
    hpwd64: string,
    kek64: string
    secret: any
}

//TODO: rename this
export interface AuthenticationResult {
    token: string,
    token2: string //TODO awaiting session with Michael to rename (Client's 1/2 of the KeK)
}