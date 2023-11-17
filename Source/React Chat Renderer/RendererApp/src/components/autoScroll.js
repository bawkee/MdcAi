import React, { useState, useRef, useEffect } from 'react';

function AutoScrollComponent({ children, autoScroll, setAutoScroll }) {
    const [userScrolledUp, setUserScrolledUp] = useState(false);

    useEffect(() => {
        const handleScroll = () => {
            let scrollUp = window.scrollY + window.innerHeight < document.documentElement.scrollHeight - 1;
            setUserScrolledUp(scrollUp);
            setAutoScroll(!scrollUp);
        };

        window.addEventListener('scroll', handleScroll);
        return () => {
            window.removeEventListener('scroll', handleScroll);
        };
    }, []);

    useEffect(() => {
        if (!userScrolledUp && autoScroll)
            scrollToBottom();
    }, [children, autoScroll]);

    const scrollToBottom = () => {
        window.scrollTo(0, document.documentElement.scrollHeight);
    };

    return (
        <div className='auto-scroll-container'>
            {children}
        </div>
    );
}

export default AutoScrollComponent;
