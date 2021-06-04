function wrapPbkdf2HmacSha256(password, saltArray64, iterations, len) {
    
    //var u8pwd = Uint8Array.from(atob(password), c => c.charCodeAt(0));
    var u8salt = Uint8Array.from(atob(saltArray64), c => c.charCodeAt(0));
    var passwordArray = new TextEncoder().encode(password);

    var hashed = asmCrypto.Pbkdf2HmacSha256(passwordArray, u8salt, iterations, len);

    var base64 = btoa(String.fromCharCode.apply(null, hashed));
    
    //console.log(hashed);
    //console.log(base64);
    return base64;
}