import React from 'react';
import {Container} from 'react-bootstrap';

function Footer(props: any) {

    return (
        <footer className="footer px-0 px-lg-3">
            <Container fluid>
                <nav>
                    <ul className="footer-menu">
                        <li>
                            <a href="#pablo" onClick={(e) => e.preventDefault()}>
                                Home
                            </a>
                        </li>
                    </ul>
                    <p className="copyright text-center">
                        © {new Date().getFullYear()}{" "}
                        <a href="https://www.youver.se">Youverse</a>, own your story
                    </p>
                </nav>
            </Container>
        </footer>
    );
}

export default Footer;