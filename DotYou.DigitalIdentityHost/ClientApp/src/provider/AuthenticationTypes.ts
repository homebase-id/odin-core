//This file just holds the classes returned from the DI server api
//They are Capital-Camel cased until I update the DI with json attributes
export interface ClientNoncePackage {
    saltPassword64: string,
    saltKek64: string,
    nonce64: string
}

export interface AuthenticationReplyNonce {
    nonce64: string
    nonceHashedPassword64: string
    keK64: string
    hashedPassword64: string
}

//TODO: rename this
export interface AuthenticationResult {
    token: string,
    token2: string //TODO awaiting session with Michael to rename (Client's 1/2 of the KeK)
}