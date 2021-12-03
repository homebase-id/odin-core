import React, {Component, useEffect, useState} from 'react';
import {Col, Container, Row, Spinner} from "react-bootstrap";
import {Route, Routes} from "react-router";
import routes, {RouteDefinition} from './Routes';
import Dashboard from "./Dashboard";
import {useAppStateStore} from "../provider/AppStateStore";

function Layout(props: any) {

    const [isInitializing, setIsInitializing] = useState(true);
    const state = useAppStateStore();
    useEffect(() => {
        const init = async () => {
            await state.initialize();
            setIsInitializing(false);
        };
        init();
    }, []);

    const renderRoutes = (routes: RouteDefinition[]) => {
        return routes.map((prop: RouteDefinition, key: any) => {
            return (<Route path={prop.path} element={prop.component} key={key}/>);
        });
    };

    if (isInitializing) {
        return <Container className="h-100 align-content-center text-center">
            <Spinner as="span" animation="border" size="sm" role="status" aria-hidden="true"/>
        </Container>
    }

    if (state.isAuthenticated) {
        return (
            <Layout>
                <Routes>
                    <Route path='/admin/dashboard' element={<Dashboard/>}/>
                    <Route path='/admin/' element={<Dashboard/>}/>
                    <Route path='/admin' element={<Dashboard/>}/>
                </Routes>
            </Layout>
        );
    }

    return (<div>Access Denied</div>);
}

export default Layout;