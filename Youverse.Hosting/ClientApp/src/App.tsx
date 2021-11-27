import React, {useEffect, useState} from 'react';
import {Route} from 'react-router-dom';
import Layout from './components/Layout';
import Home from './components/Home';

import {Spinner} from 'react-bootstrap';
import './custom.css'
import Login from "./components/Login";
import {Container} from "react-bootstrap";
import {useAppStateStore} from "./provider/AppStateStore";
import {observer} from "mobx-react-lite";
import Profile from "./components/Profile";
import PrivacySettings from "./components/PrivacySettings";
import AppLogin from "./components/AppLogin";
import Outbox from "./components/Outbox";
import Cookies from 'universal-cookie';
const cookies = new Cookies();

function App() {

    const [isInitializing, setIsInitializing] = useState(true);
    const [notification, setNotification] = useState("");

    let socket;
    let uri = "wss://" + window.location.host + "/api/live/notifications";
    cookies.set("AppId", "adminapp");
    cookies.set("deviceUid", "admin-device-id");
    
    //Pass the appid and deviceid as a cookie?  technically it's not needed because I will use the session token

    const connect = () => {
        socket = new WebSocket(uri);

        socket.onopen = function (event) {
            console.log("opened connection to " + uri);
        };

        socket.onclose = function (event) {
            console.log("closed connection from " + uri);
        };

        socket.onmessage = function (event) {
            setNotification(event.data);
            console.log(event.data);
        };

        socket.onerror = function (event) {
            console.log("error: " + event.data);
        };
    }

    const state = useAppStateStore();
    useEffect(() => {
        const init = async () => {
            await state.initialize();
            setIsInitializing(false);
        };

        init();
        connect();

    }, []);

    if (isInitializing) {
        return <Container className="h-100 align-content-center text-center">
            <Spinner
                as="span"
                animation="border"
                size="sm"
                role="status"
                aria-hidden="true"
            />
        </Container>
    }

    //HACK: if we're asking to authenticate a device
    if (window.location.href.toLowerCase().includes("?dl777")) {

        if (state.isAuthenticated && state.deviceToken) {
            //redirect to the app
            //todo: this is duplicate code
            console.log('device is authenticated; redirecting')
            window.location.href = "dotyou://auth/" + state.deviceToken;
            return;
        }

        return (
            <Container className="h-100 align-content-center text-center">
                <AppLogin/>;
            </Container>
        );
    }

    if (state.isAuthenticated) {
        return (
            <Layout message={notification}>
                <Route exact path='/' component={Home}/>
                <Route exact path='/profile' component={Profile}/>
                <Route exact path='/outbox' component={Outbox}/>
                <Route exact path='/privacy' component={PrivacySettings}/>
            </Layout>
        );
    }

    return (
        <Container className="h-100 align-content-center text-center">
            <Login/>
        </Container>
    );
}

// @ts-ignore
export default observer(App);