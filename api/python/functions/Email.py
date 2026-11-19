from flask_mail import Mail, Message
from flask import render_template
from config import CTA_MAIL
from functions.Log import Log


class Email:
    def __init__():
        pass

    @staticmethod
    def send_email(to: str, subject: str, body_html: str):

        mail = Mail()
        msg = Message(subject, sender=CTA_MAIL, recipients=[to])
        msg.html = body_html
        try:
            mail.send(msg)
        except Exception as e:
            Log.create(f"Error al enviar email: {e}", "EMAIL")
