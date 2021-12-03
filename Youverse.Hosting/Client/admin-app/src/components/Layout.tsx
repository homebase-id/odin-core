import React, {useEffect, useState} from 'react';
import {Container, Row, Spinner} from "react-bootstrap";
import {Route, Routes} from "react-router";
import {useAppStateStore} from "../provider/AppStateStore";
import Testboard from "./Testboard";
import Sidebar from "./Sidebar";

function Layout(props: any) {

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
            <Container fluid={true}>
                <Row>
                    <Sidebar/>
                    <main className="col-md-9 ms-sm-auto col-lg-10 px-md-4">
                        <Routes>
                            <Route path='/dashboard/*' element={<Testboard/>}/>
                        </Routes>
                    </main>
                </Row>
            </Container>
        );
    }

    return (<div>Access Denied</div>);
}

export default Layout;