function wrapPbkdf2HmacSha256(pwdArray64, saltArray64, iterations, len) {
    
    var u8pwd = Uint8Array.from(atob(pwdArray64), c => c.charCodeAt(0));
    var u8salt = Uint8Array.from(atob(saltArray64), c => c.charCodeAt(0));

    var hashed = asmCrypto.Pbkdf2HmacSha256(u8pwd, u8salt, iterations, len);

    var base64 = btoa(String.fromCharCode.apply(null, hashed));
    
    //console.log(hashed);
    //console.log(base64);
    return base64;
}