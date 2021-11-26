import React, {useEffect, useState} from 'react';
import {Button, Col, Form, FormControl, Row} from "react-bootstrap";
import axios from "axios";

type Props = {};

function Outbox(props: Props) {
    const [data, setData] = useState("some data");

    useEffect(()=>{
        loadOutbox();
    },[]);
    
    function loadOutbox()
    {
        //axios.get()
    }
    
    return <div>

        <h3>Public Info</h3>
        
        
    </div>
}

export default Outbox;