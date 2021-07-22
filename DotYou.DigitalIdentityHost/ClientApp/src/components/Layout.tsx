import React, {Component} from 'react';

import {Container} from "react-bootstrap";
import NavMenu from "./NavMenu";
import Sidebar from "./Sidebar";

export class Layout extends Component {
    static displayName = Layout.name;

    render() {
        return (
            <div>
                <Sidebar/>
                <NavMenu/>
                <Container>
                    {this.props.children}
                </Container>
            </div>
        );
    }
}
