import React from 'react';
import {Button, Image} from "react-bootstrap";

function Login(props: any) {

    function handleLoginClick() {

        
    }

    return <form className="form-signin">
        <Image className="mb-4" src="https://getbootstrap.com/docs/4.0/assets/brand/bootstrap-solid.svg" alt="" width="72" height="72"/>
        <h1 className="h3 mb-3 font-weight-normal">Please sign in</h1>
        <input type="password" id="inputPassword" className="form-control" placeholder="Password" required/>
        <Button className="btn btn-lg btn-primary btn-block mt-3" onClick={handleLoginClick}>Sign in</Button>
        {/*<p className="mt-5 mb-3 text-muted">&copy; 2017-2018</p>*/}
    </form>
}

export default Login;