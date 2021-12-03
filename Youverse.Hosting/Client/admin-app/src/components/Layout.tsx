import React, {Component, useEffect, useState} from 'react';
import {Col, Container, Row, Spinner} from "react-bootstrap";
import NavMenu from "./NavMenu";
import Sidebar from "./Sidebar";
import Footer from "./Footer";
// import { Route, Switch } from 'react-router-dom';
import {Route, Routes} from "react-router";
import routes, {RouteDefinition} from './Routes';
import {BrowserRouter} from "react-router-dom";
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

    return (
        <div className="wrapper">
            <Sidebar routes={routes}/>
            <div className="main-panel">
                <NavMenu/>
                <div className="content">
                    <Routes>{renderRoutes(routes)}</Routes>
                </div>
                <Footer/>
            </div>
        </div>
        // <body>
        //     <header>
        //         <NavMenu/>
        //     </header>
        //     <Container>
        //         <Row className="align-items-start">
        //             <Col xs={1}>
        //                 <Sidebar/>
        //             </Col>
        //             <Col>
        //                 {props.children}
        //             </Col>
        //         </Row>
        //     </Container>
        // </body>
    );
}

export default Layout;
