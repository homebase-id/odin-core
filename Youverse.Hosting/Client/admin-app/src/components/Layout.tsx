import React, {Component} from 'react';

import {Col, Container, Row} from "react-bootstrap";
import NavMenu from "./NavMenu";
import Sidebar from "./Sidebar";

function Layout(props: any) {
    return (
        <body>
            <header>
                <NavMenu/>
            </header>
            <Container>
                <Row className="align-items-start">
                    <Col xs={1}>
                        <Sidebar/>
                    </Col>
                    <Col>
                        {props.children}
                    </Col>
                </Row>
            </Container>
        </body>
    );
}

export default Layout;
