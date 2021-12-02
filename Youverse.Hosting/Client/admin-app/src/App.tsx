import React, {useEffect, useState} from 'react';
import {BrowserRouter, Route, Routes} from 'react-router-dom';
import Layout from './components/Layout';
import Dashboard from './components/Dashboard';
import 'bootstrap/dist/css/bootstrap.min.css';
import "@fortawesome/fontawesome-free/css/all.min.css";
import "./assets/App.css";
import {Container,Spinner} from "react-bootstrap";
import {useAppStateStore} from "./provider/AppStateStore";
import {observer} from "mobx-react-lite";
import AppLogin from "./components/AppLogin";
import Login from "./components/Login";

function App() {

    const [isInitializing, setIsInitializing] = useState(true);

    const state = useAppStateStore();
    useEffect(() => {
        const init = async () => {
            await state.initialize();
            setIsInitializing(false);
        };
        init();
    }, []);

    if (isInitializing) {
        return <Container className="h-100 align-content-center text-center">
            <Spinner as="span" animation="border" size="sm" role="status" aria-hidden="true" />
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
            <BrowserRouter>
                <Layout>
                    <Routes>
                        <Route path='/admin' element={<Dashboard/>}/>
                        <Route path='/admin' element={<Dashboard/>}/>
                        <Route path='/admin' element={<Dashboard/>}/>
                    </Routes>
                </Layout>
            </BrowserRouter>
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