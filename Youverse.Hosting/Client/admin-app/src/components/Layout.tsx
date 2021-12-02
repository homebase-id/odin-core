import React, {Component} from 'react';
import {Col, Container, Row} from "react-bootstrap";
import NavMenu from "./NavMenu";
import Sidebar from "./Sidebar";
import Footer from "./Footer";
// import { Route, Switch } from 'react-router-dom';
import { Route, Routes } from "react-router";
import routes, { RouteDefinition } from './Routes';

function Layout(props: any) {

    const renderRoutes = (routes: RouteDefinition[]) => {
        return routes.map((prop:RouteDefinition, key:any) => {
            return (<Route path={prop.path} element={prop.component} key={key} />); 
        });
      };

    return (
        <div className="wrapper">
            <Sidebar routes={routes} />
            <div className="main-panel">
                <NavMenu />
                <div className="content">
                    <Routes>{renderRoutes(routes)}</Routes>
                </div>
                <Footer />
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
