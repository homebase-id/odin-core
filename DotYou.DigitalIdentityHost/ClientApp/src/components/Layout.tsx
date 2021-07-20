import React, { Component } from 'react';

import { NavMenu } from './NavMenu';
import {Container} from "react-bootstrap";

export class Layout extends Component {
  static displayName = Layout.name;

  render () {
    return (
      <div>
        <NavMenu />
        <Container>
          {this.props.children}
        </Container>
      </div>
    );
  }
}
