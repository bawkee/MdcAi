import React, { useState, useEffect, useCallback } from 'react';

function AutoScrollComponent({ children, autoScroll, setAutoScroll }) {
    const [userScrolledUp, setUserScrolledUp] = useState(false);

    const scrollToBottom = useCallback(() => {
        window.scrollTo(0, document.documentElement.scrollHeight);
    }, []);

    useEffect(() => {
        const handleScroll = () => {
            const scrollUp = window.scrollY + window.innerHeight < document.documentElement.scrollHeight - 1;
            setUserScrolledUp(scrollUp);
            setAutoScroll(!scrollUp);
        };

        window.addEventListener('scroll', handleScroll);
        return () => {
            window.removeEventListener('scroll', handleScroll);
        };
    }, [setAutoScroll]);

    useEffect(() => {
        if (!userScrolledUp && autoScroll)
            scrollToBottom();
    }, [children, autoScroll, userScrolledUp, scrollToBottom]);

    return (
        <div className='auto-scroll-container'>
            {children}
        </div>
    );
}

export default AutoScrollComponent;
