import React, { Component } from 'react';
import {Image} from "react-bootstrap";

export class Home extends Component {
  static displayName = Home.name;

  render () {
    return (
      <div>
        <Image src="https://images.gr-assets.com/hostedimages/1568664189ra/28157476.gif"/>
        <h1>Welcome</h1>
      </div>
    );
  }
}
