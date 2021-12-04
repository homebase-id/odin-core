import React from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faHouseUser, faGrinStars, faPeopleArrows } from '@fortawesome/free-solid-svg-icons'
import { Link, LinkProps, NavLink, useMatch, useResolvedPath } from 'react-router-dom';


function ActiveDetectLink({ children, to, ...props }: LinkProps) {
    let resolved = useResolvedPath(to);
    let match = useMatch({ path: resolved.pathname, end: true });

    console.log('resolved', resolved.pathname)
    console.log('match', match)

    let css = "nav-link py-3 border-bottom";
    css += match ? " active" : "";
    return (
        <Link className={css} to={to} {...props}>
            {children}
        </Link>
    );
}
  
function Sidebar(props: any) {

    return (
        <div className="-flex flex-column flex-shrink-0 bg-light">
            <a href="/" className="d-block p-3 link-dark text-decoration-none" title="" data-bs-toggle="tooltip" data-bs-placement="right" data-bs-original-title="Icon-only">
                <FontAwesomeIcon icon={faGrinStars} size="2x" />
                <span className="visually-hidden">Youverse</span>
            </a>
            <ul className="nav nav-pills nav-flush flex-column mb-auto text-center">
                <li className="nav-item">
                    <ActiveDetectLink to="/owner/dashboard">
                        <FontAwesomeIcon icon={faHouseUser} size="2x" />
                    </ActiveDetectLink>
                </li>
                <li className="nav-item">
                    <ActiveDetectLink to="/owner/connections">
                        <FontAwesomeIcon icon={faPeopleArrows} size="2x" />
                    </ActiveDetectLink>
                </li>
                <li className="nav-item">
                    <ActiveDetectLink to="/owner/security">
                        <FontAwesomeIcon icon={faHouseUser} size="2x" />
                    </ActiveDetectLink>
                </li>
                <li className="nav-item">
                    <ActiveDetectLink to="/owner/dashboard">
                        <FontAwesomeIcon icon={faHouseUser} size="2x" />
                    </ActiveDetectLink>
                </li>
                <li className="nav-item">
                    <ActiveDetectLink to="/owner/dashboard">
                        <FontAwesomeIcon icon={faHouseUser} size="2x" />
                    </ActiveDetectLink>
                </li>
            </ul>
            <div className="dropdown border-top">
                <a href="#" className="d-flex align-items-center justify-content-center p-3 link-dark text-decoration-none dropdown-toggle" id="dropdownUser3" data-bs-toggle="dropdown" aria-expanded="false">
                    <img src="https://github.com/mdo.png" alt="mdo" className="rounded-circle" width="24" height="24" />
                </a>
                <ul className="dropdown-menu text-small shadow" aria-labelledby="dropdownUser3">
                    <li><a className="dropdown-item" href="#">New project...</a></li>
                    <li><a className="dropdown-item" href="#">Settings</a></li>
                    <li><a className="dropdown-item" href="#">Profile</a></li>
                    <li><hr className="dropdown-divider" /></li>
                    <li><a className="dropdown-item" href="#">Sign out</a></li>
                </ul>
            </div>
        </div>
    );
}

export default Sidebar;

