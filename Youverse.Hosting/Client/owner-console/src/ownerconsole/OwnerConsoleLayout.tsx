import React, {useEffect, useState} from 'react';
import {Container, Row, Col, Spinner} from "react-bootstrap";
import {Route, Routes} from "react-router";
import {useAppStateStore} from "../provider/AppStateStore";
import Testboard from "./Testboard";
import Connections from "./Connections";

import Sidebar from "./Sidebar";
import "../assets/OwnerConsole.css";

function OwnerConsoleLayout(props: any) {

    const state = useAppStateStore();
    const [initialized, setInitialized] = useState<boolean>(false);

    useEffect(() => {
        if (!initialized) {
            init();
        }
    }, []);

    const init = async () => {
        await state.initialize();
        setInitialized(true);
    };

    if (!initialized) {
        return (
            <Container className="mt-5 h-300 align-content-center text-center">
                <Spinner animation="border" role="status">
                    <span className="visually-hidden">Loading...</span>
                </Spinner>
            </Container>);
    }

    if (state.isAuthenticated) {
        return (
            <Row className="wrapper g-0 ">
                <Col md={1} className="d-none justify-content-center d-md-flex g-0">
                    <Sidebar />
                </Col>
                <Col as="main" md={11} className="no-gutters">
                    <Routes>
                        <Route path='/' element={<Testboard />} />
                        <Route path='/dashboard/*' element={<Testboard />} />
                        <Route path='/connections/*' element={<Connections />} />
                    </Routes>
                </Col>
            </Row>
        );
    }

    //className="col-md-11 ms-sm-auto col-lg-11 px-md-4

    return (<div>Access Denied</div>);
}

export default OwnerConsoleLayout;