import React, {Component} from 'react';

import {Col, Container, Row} from "react-bootstrap";
import NavMenu from "./NavMenu";
import Sidebar from "./Sidebar";

function Layout(props: { message: string, children: any }) {
    return (
        <div>
            <Container>
                <NavMenu/>
                <Row className="align-items-start">
                    <Col xs={1}>
                        <Sidebar/>
                    </Col>
                    <Col>
                        <b>
                            Message: {props.message}
                        </b>
                        
                        {props.children}
                    </Col>
                </Row>
            </Container>
        </div>
    );
}

export default Layout;
